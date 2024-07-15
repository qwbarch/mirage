module Predictor.PolicyFileHandler
open Predictor.Domain
open System.IO
open System
open Utilities
open FSharpPlus
open System.Collections.Generic
open Predictor.Config
open Mirage.Core.Async.AtomicFile
open Newtonsoft.Json

let mutable fileHandler: MailboxProcessor<PolicyFileMessage> = Operators.Unchecked.defaultof<_>

let toCompressedObsFileFormat (obs: CompressedObservation) : CompressedObservationFileFormat =
    {   time = obs.time
        spokeEmbedding = obs.spokeEmbedding
        heardEmbedding = obs.heardEmbedding
        lastSpoke = obs.lastSpoke
        lastHeard = obs.lastHeard
    }

let fromCompressedObsFileFormat (obs: CompressedObservationFileFormat) : CompressedObservation =
    {   time = obs.time
        spokeEmbedding = obs.spokeEmbedding
        heardEmbedding = obs.heardEmbedding
        lastSpoke = obs.lastSpoke
        lastHeard = obs.lastHeard
    }

let createDirIfDoesNotExist (par: string) (dir: string) =
    let fullPath = Path.Combine(par, dir)
    try 
        let _ = Directory.CreateDirectory fullPath
        ()
    with
    | ex -> logError $"Could not create directory {ex}"

let readFileData (dir: string) (fileName: string) =
    async {
        try
            let fullPath = Path.Combine(dir, fileName)
            let fileReader = new StreamReader(fullPath)
            let! contents = Async.AwaitTask <| fileReader.ReadToEndAsync()
            fileReader.Close()
            let asFileFormatOption =
                try
                    Some <| JsonConvert.DeserializeObject<PolicyFileFormat>(contents)
                with
                | ex -> 
                    logWarning <| "Unable to parse '" + fileName.ToString() + "'. Deleting..."
                    try 
                        File.Delete fullPath
                    with
                    | deleteEx -> logWarning $"Could not delete '{fileName.ToString()}'. {deleteEx.Message}"

                    None
            
            return asFileFormatOption
        with
        | ex -> 
            logWarning <| sprintf $"Could not read file data. {ex.ToString()}"
            return None
    }

let readStoredPolicy 
    (dir: string)
    (logWarning: string -> unit) = async {
    do! removeTempFiles dir logInfo logWarning

    let dateToFileInfo = Dictionary()
    let fileToData = List()
    let filesSet = SortedSet()
    let mutable counter = 0
    let add (fileName: string) (asFileFormat: PolicyFileFormat) =
        let fileInfo : Domain.PolicyFileInfo =
            {   creationDate = asFileFormat.creationDate
                name = fileName
            }
        for (observation, _) in asFileFormat.data do
            dateToFileInfo.Add(observation.time, fileInfo)
            counter <- counter + 1
        fileToData.Add(asFileFormat.data)
        let _ = filesSet.Add(fileInfo)
        ()

    try
        let files = Directory.GetFiles(dir)
        for file in files do
            let extension = Path.GetExtension(file)
            if extension = ".json" then
                let! asFileFormatOption = readFileData dir file
                match asFileFormatOption with
                | None -> ()
                | Some asFileFormat -> add file asFileFormat
                ()
    with
    | anyex -> logError <| "Something went wrong while reading stored files: " + anyex.ToString()


    logInfo <| "Read " + counter.ToString() + " data points"
    let format = {
        dateToFileInfo = dateToFileInfo
        files = filesSet
    }
    return format, fileToData
}

let toCompressedObs (observation: Observation) (context: (CompressedObservationFileFormat * FutureAction) array) : CompressedObservation =
    let isNotPrev (x : ObsEmbedding) =
        match x with
        | Prev -> false
        | Value _ -> true

    let unObsEmbedding (x: ObsEmbedding option) : Option<string * TextEmbedding> option =
        match x with
        | None -> None
        | Some obsEmbedding ->
            match obsEmbedding with
            | Prev -> None
            | Value res -> Some res

    let lastSpokeEncoding = 
        unObsEmbedding << map (fun x -> (fst x).spokeEmbedding) 
            <| Array.tryFindBack (fun (obs, _) -> isNotPrev obs.spokeEmbedding) context
    let lastHeardEncoding = 
        unObsEmbedding << map (fun x -> (fst x).heardEmbedding) 
            <| Array.tryFindBack (fun (obs, _) -> isNotPrev obs.heardEmbedding) context
    {   time = observation.time
        spokeEmbedding = toObsEmbedding lastSpokeEncoding observation.spokeEmbedding
        heardEmbedding = toObsEmbedding lastHeardEncoding observation.heardEmbedding
        lastSpoke = observation.lastSpoke
        lastHeard = observation.lastHeard
    }

let assureStorageSize (dir: string) (storageLimitAsBytes: int64) : Async<unit> =
    async {
        try
            let dirInfo = DirectoryInfo(dir)
            let fileInfos = dirInfo.GetFiles() |> Array.sortBy (fun fileInfo -> fileInfo.CreationTimeUtc)
            let totalStorage = fileInfos |> Array.sumBy (fun fileInfo -> fileInfo.Length)

            let mutable currentStorage = totalStorage
            for fileInfo in fileInfos do
                if storageLimitAsBytes < currentStorage then
                    currentStorage <- currentStorage - fileInfo.Length
                    try
                        fileInfo.Delete()
                        logInfo <| sprintf $"Deleted {fileInfo}. Current storage is now {currentStorage} bytes"
                    with
                    | ex -> logWarning $"Could not delete the file. {ex.ToString()}"
            ()
        with
        | ex -> logError $"Something went wrong in the storage limiter. {ex.ToString()}"
    }

let storageLimiter (dir: string) (storageLimitAsBytes: int64) =
    let rec loop () = 
        async {
            do! assureStorageSize dir storageLimitAsBytes
            do! Async.Sleep 60000
            do! loop()
        }
    loop()

let createFileHandler 
    (fileState: PolicyFileState)
    (dir: string)
    (storageLimitAsBytes: int64) =
    Async.Start <| storageLimiter dir storageLimitAsBytes
    MailboxProcessor<PolicyFileMessage>.Start(fun inbox ->
        let rec loop () =
            async {
                let! message = inbox.Receive()
                try
                    match message with
                    | Update (obsTime: DateTime, newAction: FutureAction) ->
                        if fileState.dateToFileInfo.ContainsKey(obsTime) then
                            // Update a file
                            let fileInfo = fileState.dateToFileInfo[obsTime]
                            let! prevDataFile = readFileData dir fileInfo.name
                            match prevDataFile with
                            | None -> ()
                            | Some policyFileFormat ->
                                let prevData = policyFileFormat.data
                                let newData = Array.copy prevData
                                let obsIndex = Array.findIndex (fun (obs, _) -> obs.time = obsTime) prevData
                                let (existingObs, _) = prevData.[obsIndex]
                                newData.[obsIndex] <- (existingObs, newAction)

                                let newFileData = {
                                    creationDate = fileInfo.creationDate
                                    data = newData
                                }
                                let newFileString = JsonConvert.SerializeObject(newFileData, Formatting.Indented)
                                let path = Path.Combine(dir, fileInfo.name)

                                let! _ = atomicFileWrite path newFileString true logInfo logError
                                ()
                        ()
                    | Add (observation, futureAction) ->
                        let! newFileCondition =
                            async {
                                if fileState.files.Count = 0 then
                                    return true
                                else
                                    let fileInfo = fileState.files.Max
                                    let! prevDataFile = readFileData dir fileInfo.name
                                    match prevDataFile with
                                    | None -> return true
                                    | Some policyFileFormat -> return policyFileFormat.data.Length >= config.FILE_SPLIT_SIZE
                            }

                        if newFileCondition then
                            // Create a new file
                            let now = DateTime.UtcNow
                            let newFileName = now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString() + ".json"
                            let newFileData = {
                                creationDate = now
                                data = Array.init 1 (fun _ -> (toCompressedObsFileFormat <| toCompressedObs observation Array.empty, futureAction))
                            }
                            let newFileInfo = {
                                creationDate = now
                                name = newFileName
                            }
                            let newFileString = JsonConvert.SerializeObject(newFileData, Formatting.Indented)
                            let path = Path.Combine(dir, newFileName)

                            let! _ = atomicFileWrite path newFileString true logInfo logError

                            let _ = fileState.files.Add(newFileInfo)
                            let _ = fileState.dateToFileInfo.TryAdd(observation.time, newFileInfo)
                            ()
                        else
                            // Append to the latest file
                            let fileInfo = fileState.files.Max
                            let! prevDataFile = readFileData dir fileInfo.name
                            match prevDataFile with
                            | None -> ()
                            | Some policyFileFormat ->
                                let prevData = policyFileFormat.data
                                let newObs = toCompressedObsFileFormat <| toCompressedObs observation prevData
                                let newFileData = {
                                    creationDate = fileInfo.creationDate
                                    data = Array.append prevData [|(newObs, futureAction)|]
                                }
                                let newFileString = JsonConvert.SerializeObject(newFileData, Formatting.Indented)
                                let path = Path.Combine(dir, fileInfo.name)
                                let! _ = atomicFileWrite path  newFileString true logInfo logError

                                let _ = fileState.dateToFileInfo.TryAdd(observation.time, fileInfo)
                                ()
                with
                | ex -> logError $"Something went wrong in the file handler {ex.ToString()}"
                do! loop()
            }
        loop()
    )