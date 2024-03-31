module Mirage.Core.Field

open System
open FSharpPlus
open Mirage.Utilities.Operator

/// A lazily evaluated string, to prevent constructing the error message when not needed.
type ErrorMessage = Unit -> string

/// A getter function that should be used within a monad computation builder.
type Getter<'A> = Unit -> Result<'A, ErrorMessage>

/// A field is a type-alias of a reference, holding a value that might not be present.
type Field<'A> = private { mutable value: option<'A> }

/// Provides a getter and a field, for the purpose of including a stack trace for easier debugging when a value is missing,
/// compared to a stack trace received from a <b>NullReferenceException</b>.<br />
let useField<'A> () : Tuple<Getter<'A>, Field<'A>> =
    let reference = { value = None }
    let getter () =
        let errorMessage () =
            let frames = Diagnostics.StackTrace(true).GetFrames()
            let mutable skipFrames = 0
            let mutable i = 0
            // Skip the stack frames that comes before the caller method.
            while i < frames.Length && skipFrames = 0 do
                let fileName = frames[i].GetFileName()
                if not (isNull fileName) && String.endsWith "Logger.fs" fileName then
                    skipFrames <- i + 1
                &i += 1
            Diagnostics.StackTrace(skipFrames, true).ToString()
        Option.toResultWith errorMessage reference.value
    (getter, reference)

/// A setter function that sets the field with an optional value. This is equivalent to the same operator in Haskell lenses.
let (.=) (field: Field<'A>) (value: Option<'A>) = field.value <- value

/// A setter function that sets the field with a value, implicitly setting the field as <i>Some</i> value.
/// This is equivalent to the same operator in Haskell lenses.
let (?=) (field: Field<'A>) (value: 'A) = field.value <- Some value