namespace Mire.Demo.Minesweeper

// ---------------------------------------------------------------------------
// Mire.Demo.Minesweeper — pure game domain. No I/O, no rendering: just the board
// model and the rules (mine placement, adjacency counts, flood-fill reveal,
// flagging, chording, win/loss). All randomness is injected via System.Random
// so the logic stays deterministic and unit-testable.
// ---------------------------------------------------------------------------

type CellState =
    | Hidden
    | Flagged
    | Revealed

type Cell =
    { Mine: bool
      Adjacent: int // count of mines in the 8 neighbours (valid once mines placed)
      State: CellState }

type GameStatus =
    | Playing
    | Won
    | Lost

type Difficulty =
    | Beginner
    | Intermediate
    | Expert

type Board =
    { Rows: int
      Cols: int
      Mines: int
      Cells: Cell[,] // indexed [row, col]
      Status: GameStatus
      Placed: bool // mines are deferred until the first reveal (first-click-safe)
      Difficulty: Difficulty }

module Board =

    /// (rows, cols, mines) for each difficulty preset.
    let dimensions (d: Difficulty) : int * int * int =
        match d with
        | Beginner -> 9, 9, 10
        | Intermediate -> 16, 16, 40
        | Expert -> 16, 30, 99

    let private hiddenCell =
        { Mine = false
          Adjacent = 0
          State = Hidden }

    /// A fresh, un-mined board for the given difficulty (everything Hidden).
    let empty (d: Difficulty) : Board =
        let rows, cols, mines = dimensions d

        { Rows = rows
          Cols = cols
          Mines = mines
          Cells = Array2D.create rows cols hiddenCell
          Status = Playing
          Placed = false
          Difficulty = d }

    let inline private inBounds (b: Board) (r: int) (c: int) =
        r >= 0 && r < b.Rows && c >= 0 && c < b.Cols

    /// The (up to) 8 in-bounds neighbours of (r, c).
    let neighbors (b: Board) (r: int) (c: int) : (int * int) list =
        [ for dr in -1 .. 1 do
              for dc in -1 .. 1 do
                  if (dr <> 0 || dc <> 0) && inBounds b (r + dr) (c + dc) then
                      yield (r + dr, c + dc) ]

    /// Recompute every cell's Adjacent mine count from the Mine flags.
    let private computeAdjacency (b: Board) : Board =
        let cells =
            b.Cells
            |> Array2D.mapi (fun r c cell ->
                let count =
                    neighbors b r c
                    |> List.filter (fun (nr, nc) -> b.Cells.[nr, nc].Mine)
                    |> List.length

                { cell with Adjacent = count })

        { b with Cells = cells }

    /// Place mines at random, never on the safe cell (sr, sc) or its neighbours,
    /// then fill in adjacency counts. Randomness is injected for determinism.
    let placeMines (rng: System.Random) (sr: int) (sc: int) (b: Board) : Board =
        let safe = Set.ofList ((sr, sc) :: neighbors b sr sc)

        let candidates =
            [ for r in 0 .. b.Rows - 1 do
                  for c in 0 .. b.Cols - 1 do
                      if not (safe.Contains(r, c)) then
                          yield (r, c) ]

        // Fisher–Yates shuffle, then take the first `Mines` positions.
        let arr = List.toArray candidates

        for i in arr.Length - 1 .. -1 .. 1 do
            let j = rng.Next(i + 1)
            let tmp = arr.[i]
            arr.[i] <- arr.[j]
            arr.[j] <- tmp

        let mineCount = min b.Mines arr.Length
        let cells = Array2D.copy b.Cells

        for k in 0 .. mineCount - 1 do
            let (r, c) = arr.[k]
            cells.[r, c] <- { cells.[r, c] with Mine = true }

        computeAdjacency { b with Cells = cells; Placed = true }

    /// Win when every non-mine cell has been revealed.
    let checkWin (b: Board) : Board =
        if b.Status <> Playing then
            b
        else
            let won =
                b.Cells
                |> Seq.cast<Cell>
                |> Seq.forall (fun cell -> cell.Mine || cell.State = Revealed)

            if won then { b with Status = Won } else b

    /// Reveal every mine (used on loss to show the full board).
    let private revealAllMines (b: Board) : Board =
        let cells = Array2D.copy b.Cells

        for r in 0 .. b.Rows - 1 do
            for c in 0 .. b.Cols - 1 do
                if cells.[r, c].Mine then
                    cells.[r, c] <- { cells.[r, c] with State = Revealed }

        { b with Cells = cells }

    /// Iterative flood-fill: reveal the connected region of zero-adjacency cells
    /// starting at (r, c), plus the numbered border around it. Mutates a copy.
    let private floodReveal (cells: Cell[,]) (b: Board) (r: int) (c: int) : unit =
        let stack = System.Collections.Generic.Stack<int * int>()
        stack.Push(r, c)

        while stack.Count > 0 do
            let (cr, cc) = stack.Pop()
            let cell = cells.[cr, cc]
            // Only Hidden cells reveal; Flagged and already-Revealed are skipped.
            if cell.State = Hidden then
                cells.[cr, cc] <- { cell with State = Revealed }
                // Only zero-adjacency cells propagate to their neighbours.
                if cell.Adjacent = 0 then
                    for (nr, nc) in neighbors b cr cc do
                        if cells.[nr, nc].State = Hidden then
                            stack.Push(nr, nc)

    /// Reveal the cell at (r, c). On the first reveal, `rng` is used to lazily
    /// place mines (first-click-safe). No-op when the game is over, or the cell
    /// is flagged or already revealed.
    let reveal (rng: System.Random) (r: int) (c: int) (b: Board) : Board =
        if b.Status <> Playing || not (inBounds b r c) then
            b
        else
            let b = if b.Placed then b else placeMines rng r c b
            let cell = b.Cells.[r, c]

            match cell.State with
            | Revealed
            | Flagged -> b
            | Hidden ->
                if cell.Mine then
                    let cells = Array2D.copy b.Cells
                    cells.[r, c] <- { cell with State = Revealed }
                    revealAllMines { b with Cells = cells; Status = Lost }
                else
                    let cells = Array2D.copy b.Cells
                    floodReveal cells b r c
                    checkWin { b with Cells = cells }

    /// Toggle a flag on a Hidden cell (or remove one). No-op on revealed cells or
    /// when the game is over.
    let toggleFlag (r: int) (c: int) (b: Board) : Board =
        if b.Status <> Playing || not (inBounds b r c) then
            b
        else
            let cell = b.Cells.[r, c]

            match cell.State with
            | Revealed -> b
            | state ->
                let cells = Array2D.copy b.Cells

                cells.[r, c] <-
                    { cell with
                        State = (if state = Flagged then Hidden else Flagged) }

                { b with Cells = cells }

    /// Chord: on a revealed number whose adjacent flag count equals the number,
    /// reveal all non-flagged neighbours at once (can detonate a mine if a flag
    /// is misplaced).
    let chord (rng: System.Random) (r: int) (c: int) (b: Board) : Board =
        if b.Status <> Playing || not (inBounds b r c) then
            b
        else
            let cell = b.Cells.[r, c]

            if cell.State <> Revealed || cell.Adjacent = 0 then
                b
            else
                let ns = neighbors b r c

                let flagged =
                    ns
                    |> List.filter (fun (nr, nc) -> b.Cells.[nr, nc].State = Flagged)
                    |> List.length

                if flagged <> cell.Adjacent then
                    b
                else
                    // Reveal each hidden, non-flagged neighbour; reveal handles
                    // loss/flood-fill and threads the board through.
                    ns |> List.fold (fun acc (nr, nc) -> reveal rng nr nc acc) b

    /// Number of flags currently placed on the board.
    let flagsPlaced (b: Board) : int =
        b.Cells
        |> Seq.cast<Cell>
        |> Seq.filter (fun cell -> cell.State = Flagged)
        |> Seq.length

    /// Mines minus flags placed (the classic "mine counter"; can go negative).
    let minesRemaining (b: Board) : int = b.Mines - flagsPlaced b
