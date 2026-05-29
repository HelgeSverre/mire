namespace Mire.SpreadsheetDemo

open System
open System.Text.RegularExpressions

/// An A1-style spreadsheet model plus a small formula evaluator: numbers,
/// + - * / and parentheses, cell references (A1), ranges (A1:B3), and the
/// functions SUM / AVG / MIN / MAX / COUNT. Cells are recomputed with memoization
/// and cycle detection.
module Sheet =

    let cols = 8 // A..H
    let rows = 30

    let colLetter (c: int) : string = string (char (int 'A' + c))
    let name (r: int) (c: int) : string = sprintf "%s%d" (colLetter c) (r + 1)

    /// Parse an "A1" reference to a 0-based (row, col).
    let parseRef (s: string) : (int * int) option =
        let m = Regex.Match(s.Trim().ToUpperInvariant(), @"^([A-Z]+)(\d+)$")
        if not m.Success then
            None
        else
            let col = (m.Groups.[1].Value |> Seq.fold (fun a ch -> a * 26 + (int ch - int 'A' + 1)) 0) - 1
            let row = (int m.Groups.[2].Value) - 1
            Some(row, col)

    type Value =
        | Num of float
        | Str of string
        | Err of string
        | Blank

    // --- tokenizer + recursive-descent evaluator -------------------------

    type private Tok =
        | TNum of float
        | TRef of (int * int)
        | TRange of (int * int) * (int * int)
        | TFn of string
        | TOp of char
        | TLParen
        | TRParen
        | TComma

    exception private EvalErr of string

    let private lex (s: string) : Tok list option =
        let n = s.Length
        let toks = ResizeArray<Tok>()
        let mutable i = 0
        let mutable ok = true
        while ok && i < n do
            let c = s.[i]
            if Char.IsWhiteSpace c then i <- i + 1
            elif c = '(' then toks.Add TLParen; i <- i + 1
            elif c = ')' then toks.Add TRParen; i <- i + 1
            elif c = ',' then toks.Add TComma; i <- i + 1
            elif c = '+' || c = '-' || c = '*' || c = '/' then toks.Add(TOp c); i <- i + 1
            elif Char.IsDigit c || c = '.' then
                let start = i
                while i < n && (Char.IsDigit s.[i] || s.[i] = '.') do i <- i + 1
                match Double.TryParse(s.Substring(start, i - start), Globalization.CultureInfo.InvariantCulture) with
                | true, v -> toks.Add(TNum v)
                | _ -> ok <- false
            elif Char.IsLetter c then
                let start = i
                while i < n && Char.IsLetterOrDigit s.[i] do i <- i + 1
                let word = s.Substring(start, i - start)
                if i < n && s.[i] = '(' then
                    toks.Add(TFn(word.ToUpperInvariant()))
                else
                    match parseRef word with
                    | Some a ->
                        if i < n && s.[i] = ':' then
                            i <- i + 1
                            let s2 = i
                            while i < n && Char.IsLetterOrDigit s.[i] do i <- i + 1
                            match parseRef (s.Substring(s2, i - s2)) with
                            | Some b -> toks.Add(TRange(a, b))
                            | None -> ok <- false
                        else
                            toks.Add(TRef a)
                    | None -> ok <- false
            else
                ok <- false
        if ok then Some(List.ofSeq toks) else None

    let private rangeCells ((r1, c1): int * int) ((r2, c2): int * int) =
        [ for r in min r1 r2 .. max r1 r2 do
              for c in min c1 c2 .. max c1 c2 -> (r, c) ]

    /// Evaluate a formula body (without the leading '='). `resolve` returns the
    /// numeric value of a referenced cell (or raises EvalErr for #CYCLE/etc).
    let private evalFormula (resolve: int * int -> float) (body: string) : Value =
        match lex body with
        | None -> Err "#SYNTAX"
        | Some toks ->
            let mutable rest = toks
            let peek () = match rest with t :: _ -> Some t | [] -> None
            let advance () =
                match rest with
                | t :: tl -> rest <- tl; t
                | [] -> raise (EvalErr "#SYNTAX")

            let rec expr () =
                let mutable v = term ()
                let mutable go = true
                while go do
                    match peek () with
                    | Some(TOp '+') -> advance () |> ignore; v <- v + term ()
                    | Some(TOp '-') -> advance () |> ignore; v <- v - term ()
                    | _ -> go <- false
                v

            and term () =
                let mutable v = factor ()
                let mutable go = true
                while go do
                    match peek () with
                    | Some(TOp '*') -> advance () |> ignore; v <- v * factor ()
                    | Some(TOp '/') ->
                        advance () |> ignore
                        let d = factor ()
                        if d = 0.0 then raise (EvalErr "#DIV/0")
                        v <- v / d
                    | _ -> go <- false
                v

            and factor () =
                match advance () with
                | TNum v -> v
                | TRef rc -> resolve rc
                | TOp '-' -> -(factor ())
                | TLParen ->
                    let v = expr ()
                    match advance () with
                    | TRParen -> v
                    | _ -> raise (EvalErr "#SYNTAX")
                | TFn fn ->
                    (match advance () with
                     | TLParen -> ()
                     | _ -> raise (EvalErr "#SYNTAX"))
                    let args = ResizeArray<float>()
                    let mutable more = (peek () <> Some TRParen)
                    while more do
                        match peek () with
                        | Some(TRange(a, b)) ->
                            advance () |> ignore
                            for cell in rangeCells a b do args.Add(resolve cell)
                        | _ -> args.Add(expr ())
                        match peek () with
                        | Some TComma -> advance () |> ignore
                        | _ -> more <- false
                    (match advance () with
                     | TRParen -> ()
                     | _ -> raise (EvalErr "#SYNTAX"))
                    let xs = List.ofSeq args
                    match fn with
                    | "SUM" -> List.sum xs
                    | "AVG" | "AVERAGE" -> if List.isEmpty xs then 0.0 else List.sum xs / float xs.Length
                    | "MIN" -> if List.isEmpty xs then 0.0 else List.min xs
                    | "MAX" -> if List.isEmpty xs then 0.0 else List.max xs
                    | "COUNT" -> float xs.Length
                    | _ -> raise (EvalErr "#NAME")
                | _ -> raise (EvalErr "#SYNTAX")

            try
                let v = expr ()
                match peek () with
                | None -> Num v
                | Some _ -> Err "#SYNTAX"
            with EvalErr e -> Err e

    /// Recompute every non-empty cell from the raw inputs (memoized, cycle-safe).
    let compute (raw: Map<int * int, string>) : Map<int * int, Value> =
        let cache = Collections.Generic.Dictionary<int * int, Value>()
        let visiting = Collections.Generic.HashSet<int * int>()
        let rec valueAt (rc: int * int) : Value =
            match cache.TryGetValue rc with
            | true, v -> v
            | _ ->
                if not (visiting.Add rc) then
                    Err "#CYCLE"
                else
                    let v =
                        match Map.tryFind rc raw with
                        | None -> Blank
                        | Some s when s.StartsWith "=" ->
                            let resolve ref =
                                match valueAt ref with
                                | Num n -> n
                                | Blank -> 0.0
                                | Str _ -> 0.0
                                | Err e -> raise (EvalErr e)
                            evalFormula resolve (s.Substring 1)
                        | Some s ->
                            match Double.TryParse(s, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture) with
                            | true, n -> Num n
                            | _ -> Str s
                    visiting.Remove rc |> ignore
                    cache.[rc] <- v
                    v
        raw |> Map.fold (fun acc k _ -> Map.add k (valueAt k) acc) Map.empty

    /// Render a value for display in a cell.
    let show (v: Value) : string =
        match v with
        | Blank -> ""
        | Str s -> s
        | Err e -> e
        | Num n ->
            if Double.IsNaN n || Double.IsInfinity n then "#NUM"
            elif n = Math.Floor n && abs n < 1e15 then sprintf "%d" (int64 n)
            else
                let s = n.ToString("0.####", Globalization.CultureInfo.InvariantCulture)
                s
