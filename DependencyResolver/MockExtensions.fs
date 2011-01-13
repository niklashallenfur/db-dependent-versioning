module MockExtensions



open NUnit.Framework
open Rhino.Mocks


type System.Object with
    member mock.Str() = mock.ToString()