module Mirage.Core.Field

open System
open System.Diagnostics
open FSharpPlus

/// A lazily evaluated error message, containing a stack-trace.
type FieldError = unit -> string

type Getter<'A> = unit -> Result<'A, FieldError>
type Setter<'A> = 'A -> unit
type OptionSetter<'A> = option<'A> -> unit

/// Create a field that provides improved error logs if the value is missing.<br />
/// This is a variant of <b>useField</b> that returns an <b>OptionSetter</b> instead of a <b>Setter</b>.
let useField_<'A> () : Tuple<Getter<'A>, OptionSetter<'A>> =
    let field = ref None
    let setter state = field.Value <- state
    let getter () =
        flip Option.toResultWith field.Value <| fun () ->
            // Remove the first line of the stack-trace, since the first line will always belong to this class itself.
            let mutable stackTrace = StackTrace(true).ToString()
            stackTrace <- stackTrace.Substring(stackTrace.IndexOf('\n') + 1)
            $"Failed to retrieve field value:\n{stackTrace}"
    (getter, setter)

/// Create a field that provides improved error logs if the value is missing.<br />
let useField<'A when 'A : null> () : Tuple<Getter<'A>, Setter<'A>> =
  let (get, set) = useField_<'A>()
  (get, set << Option.ofObj)

/// Create a field that provides improved error logs if the value is missing.<br />
/// This is a variant of <b>useField</b> that allows you to set the initial state.
let useFieldWith<'A when 'A : null> (initialState: 'A) : Tuple<Getter<'A>, Setter<'A>> =
  let (get, set) = useField()
  set initialState
  (get, set)