namespace Mire.Demo.Spreadsheet

open System
open System.Text.RegularExpressions

/// An A1-style spreadsheet model plus a small formula evaluator: numbers,
/// + - * / and parentheses, cell references (A1), ranges (A1:B3), and the
/// functions SUM / AVG / MIN / MAX / COUNT. Cells are recomputed with memoization
/// and cycle detection.
module Sheet =

    /// A cell coordinate, `(row, column)` — both 0-based. (An abbreviation, so it
    /// stays a plain tuple: usable as a `Map` key and with `fst`/`snd`.)
    type CellRef = int * int

    let columnCount = 26 // A..Z
    let rowCount = 100

    let columnLabel (col: int) : string = string (char (int 'A' + col))

    let cellName (row: int) (col: int) : string =
        sprintf "%s%d" (columnLabel col) (row + 1)

    /// Parse an "A1" reference into a 0-based `CellRef`.
    let parseCellRef (s: string) : CellRef option =
        let m = Regex.Match(s.Trim().ToUpperInvariant(), @"^([A-Z]+)(\d+)$")

        if not m.Success then
            None
        else
            let col =
                (m.Groups.[1].Value
                 |> Seq.fold (fun acc ch -> acc * 26 + (int ch - int 'A' + 1)) 0)
                - 1

            let row = (int m.Groups.[2].Value) - 1
            Some(row, col)

    type Value =
        | Num of float
        | Str of string
        | Err of string
        | Blank

    // --- tokenizer + recursive-descent evaluator -------------------------

    type private Token =
        | TNum of float
        | TRef of CellRef
        | TRange of CellRef * CellRef
        | TFn of string
        | TOp of char
        | TLParen
        | TRParen
        | TComma

    exception private EvalError of string

    let private tokenize (s: string) : Token list option =
        let n = s.Length
        let tokens = ResizeArray<Token>()
        let mutable i = 0
        let mutable ok = true

        while ok && i < n do
            let c = s.[i]

            if Char.IsWhiteSpace c then
                i <- i + 1
            elif c = '(' then
                tokens.Add TLParen
                i <- i + 1
            elif c = ')' then
                tokens.Add TRParen
                i <- i + 1
            elif c = ',' then
                tokens.Add TComma
                i <- i + 1
            elif c = '+' || c = '-' || c = '*' || c = '/' then
                tokens.Add(TOp c)
                i <- i + 1
            elif Char.IsDigit c || c = '.' then
                let start = i

                while i < n && (Char.IsDigit s.[i] || s.[i] = '.') do
                    i <- i + 1

                match Double.TryParse(s.Substring(start, i - start), Globalization.CultureInfo.InvariantCulture) with
                | true, v -> tokens.Add(TNum v)
                | _ -> ok <- false
            elif Char.IsLetter c then
                let start = i

                while i < n && Char.IsLetterOrDigit s.[i] do
                    i <- i + 1

                let word = s.Substring(start, i - start)

                if i < n && s.[i] = '(' then
                    tokens.Add(TFn(word.ToUpperInvariant()))
                else
                    match parseCellRef word with
                    | Some first ->
                        if i < n && s.[i] = ':' then
                            i <- i + 1
                            let rangeStart = i

                            while i < n && Char.IsLetterOrDigit s.[i] do
                                i <- i + 1

                            match parseCellRef (s.Substring(rangeStart, i - rangeStart)) with
                            | Some last -> tokens.Add(TRange(first, last))
                            | None -> ok <- false
                        else
                            tokens.Add(TRef first)
                    | None -> ok <- false
            else
                ok <- false

        if ok then Some(List.ofSeq tokens) else None

    let private cellsInRange ((r1, c1): CellRef) ((r2, c2): CellRef) : CellRef list =
        [ for row in min r1 r2 .. max r1 r2 do
              for col in min c1 c2 .. max c1 c2 -> (row, col) ]

    /// Cells a raw input depends on (ranges expanded). [] for non-formulas/unparseable.
    let referencesOf (input: string) : CellRef list =
        if not (input.StartsWith "=") then
            []
        else
            match tokenize (input.Substring 1) with
            | None -> []
            | Some toks ->
                toks
                |> List.collect (function
                    | TRef c -> [ c ]
                    | TRange(a, b) -> cellsInRange a b
                    | _ -> [])
                |> List.distinct

    /// Evaluate a formula body (without the leading '='). `resolveCell` returns the
    /// numeric value of a referenced cell (or raises EvalError for #CYCLE/etc).
    let private evaluateFormula (resolveCell: CellRef -> float) (body: string) : Value =
        match tokenize body with
        | None -> Err "#SYNTAX"
        | Some tokens ->
            let mutable remaining = tokens

            let peek () =
                match remaining with
                | t :: _ -> Some t
                | [] -> None

            let advance () =
                match remaining with
                | t :: rest ->
                    remaining <- rest
                    t
                | [] -> raise (EvalError "#SYNTAX")

            let rec expr () =
                let mutable value = term ()
                let mutable go = true

                while go do
                    match peek () with
                    | Some(TOp '+') ->
                        advance () |> ignore
                        value <- value + term ()
                    | Some(TOp '-') ->
                        advance () |> ignore
                        value <- value - term ()
                    | _ -> go <- false

                value

            and term () =
                let mutable value = factor ()
                let mutable go = true

                while go do
                    match peek () with
                    | Some(TOp '*') ->
                        advance () |> ignore
                        value <- value * factor ()
                    | Some(TOp '/') ->
                        advance () |> ignore
                        let divisor = factor ()

                        if divisor = 0.0 then
                            raise (EvalError "#DIV/0")

                        value <- value / divisor
                    | _ -> go <- false

                value

            and factor () =
                match advance () with
                | TNum v -> v
                | TRef cell -> resolveCell cell
                | TOp '-' -> -(factor ())
                | TLParen ->
                    let value = expr ()

                    match advance () with
                    | TRParen -> value
                    | _ -> raise (EvalError "#SYNTAX")
                | TFn fn ->
                    (match advance () with
                     | TLParen -> ()
                     | _ -> raise (EvalError "#SYNTAX"))

                    let args = ResizeArray<float>()
                    let mutable more = (peek () <> Some TRParen)

                    while more do
                        match peek () with
                        | Some(TRange(first, last)) ->
                            advance () |> ignore

                            for cell in cellsInRange first last do
                                args.Add(resolveCell cell)
                        | _ -> args.Add(expr ())

                        match peek () with
                        | Some TComma -> advance () |> ignore
                        | _ -> more <- false

                    (match advance () with
                     | TRParen -> ()
                     | _ -> raise (EvalError "#SYNTAX"))

                    let values = List.ofSeq args

                    match fn with
                    | "SUM" -> List.sum values
                    | "AVG"
                    | "AVERAGE" ->
                        if List.isEmpty values then
                            0.0
                        else
                            List.sum values / float values.Length
                    | "MIN" -> if List.isEmpty values then 0.0 else List.min values
                    | "MAX" -> if List.isEmpty values then 0.0 else List.max values
                    | "COUNT" -> float values.Length
                    | _ -> raise (EvalError "#NAME")
                | _ -> raise (EvalError "#SYNTAX")

            try
                let value = expr ()

                match peek () with
                | None -> Num value
                | Some _ -> Err "#SYNTAX"
            with EvalError e ->
                Err e

    /// Recompute every non-empty cell from the raw `inputs` (memoized, cycle-safe).
    let recalculate (inputs: Map<CellRef, string>) : Map<CellRef, Value> =
        let cache = Collections.Generic.Dictionary<CellRef, Value>()
        let visiting = Collections.Generic.HashSet<CellRef>()

        let rec valueAt (cell: CellRef) : Value =
            match cache.TryGetValue cell with
            | true, v -> v
            | _ ->
                if not (visiting.Add cell) then
                    Err "#CYCLE"
                else
                    let value =
                        match Map.tryFind cell inputs with
                        | None -> Blank
                        | Some s when s.StartsWith "=" ->
                            let resolveCell ref =
                                match valueAt ref with
                                | Num n -> n
                                | Blank -> 0.0
                                | Str _ -> 0.0
                                | Err e -> raise (EvalError e)

                            evaluateFormula resolveCell (s.Substring 1)
                        | Some s ->
                            match
                                Double.TryParse(
                                    s,
                                    Globalization.NumberStyles.Any,
                                    Globalization.CultureInfo.InvariantCulture
                                )
                            with
                            | true, n -> Num n
                            | _ -> Str s

                    visiting.Remove cell |> ignore
                    cache.[cell] <- value
                    value

        inputs |> Map.fold (fun acc cell _ -> Map.add cell (valueAt cell) acc) Map.empty

    /// Format a computed value for display in a cell.
    let formatValue (v: Value) : string =
        match v with
        | Blank -> ""
        | Str s -> s
        | Err e -> e
        | Num n ->
            if Double.IsNaN n || Double.IsInfinity n then
                "#NUM"
            elif n = Math.Floor n && abs n < 1e15 then
                sprintf "%d" (int64 n)
            else
                n.ToString("0.####", Globalization.CultureInfo.InvariantCulture)
