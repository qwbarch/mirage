module Mirage.Hook.PreInitScene

open System.Collections

let hookPreInitScene (main: IEnumerator) =
    On.PreInitSceneScript.add_Start(fun orig self ->
        orig.Invoke self
        ignore <| self.StartCoroutine main
    )