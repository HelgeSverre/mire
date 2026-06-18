namespace Mire.Agent

open Mire.Core
open Mire.Layout
open Mire.Widgets

/// Accept/reject state of a diff hunk. The app owns it (MVU); `DiffView` only renders it.
type HunkStatus =
    | Pending
    | Accepted
    | Rejected

/// One hunk of a diff: a header (e.g. `@@ -1,3 +1,4 @@ file.fs`) and its lines.
type DiffHunk =
    { Header: string
      Lines: DiffLine list
      Status: HunkStatus }

type DiffMode =
    | Unified
    | Split

/// Renders a reviewable diff — a list of `DiffHunk`s — in unified or side-by-side
/// split mode, with a per-hunk accept/reject marker and a selection highlight.
/// Pure: the app owns the hunk list, the `selected` index, and each hunk's
/// `Status`, and drives navigation/accept/reject in `update` (like `ListView`).
module DiffView =

    let private blank = { Sign = ' '; Text = "" }

    /// Split a hunk's lines into the (before, after) columns for side-by-side view:
    /// the left column keeps context + removed lines, the right keeps context +
    /// added lines, each padded with blanks to the same height. (A simple split —
    /// not LCS-aligned within a hunk.)
    let splitColumns (lines: DiffLine list) : DiffLine list * DiffLine list =
        let left = lines |> List.filter (fun l -> l.Sign <> '+')
        let right = lines |> List.filter (fun l -> l.Sign <> '-')
        let n = max (List.length left) (List.length right)

        let pad xs =
            xs @ List.replicate (n - List.length xs) blank

        pad left, pad right

    /// The marker glyph for a hunk's review status.
    let statusMark (s: HunkStatus) =
        match s with
        | Accepted -> "✓"
        | Rejected -> "✗"
        | Pending -> "·"

    let private statusStyle (theme: AppTheme) (s: HunkStatus) =
        match s with
        | Accepted -> theme.success
        | Rejected -> theme.danger
        | Pending -> theme.fgMuted

    let private lineStyle (theme: AppTheme) (sign: char) =
        match sign with
        | '+' -> theme.success
        | '-' -> theme.danger
        | _ -> theme.fgSubtle

    let private lineNode (theme: AppTheme) (l: DiffLine) : LayoutNode<'msg> =
        Text.text (sprintf "%c %s" l.Sign l.Text) (lineStyle theme l.Sign)

    /// A hunk header row: status marker + header text, full-width-highlighted when selected.
    let private headerNode (theme: AppTheme) (selected: bool) (h: DiffHunk) : LayoutNode<'msg> =
        let label = sprintf "%s %s" (statusMark h.Status) h.Header

        if selected then
            Backdrop.behind theme.selection (Text.text label theme.selection)
        else
            Text.text label (statusStyle theme h.Status)

    let private unifiedHunk (theme: AppTheme) (selected: bool) (h: DiffHunk) : LayoutNode<'msg> =
        Stack.vstack (headerNode theme selected h :: (h.Lines |> List.map (lineNode theme)))

    let private splitHunk (theme: AppTheme) (width: int) (selected: bool) (h: DiffHunk) : LayoutNode<'msg> =
        let colW = max 1 ((width - 1) / 2) // two columns + a 1-cell gutter
        let left, right = splitColumns h.Lines

        let rows =
            List.zip left right
            |> List.map (fun (l, r) ->
                Stack.hstackOf
                    [ Stack.sized (Length.Cells colW) (lineNode theme l)
                      Stack.sized (Length.Cells 1) (Text.text "│" theme.border)
                      Stack.sized Length.Fill (lineNode theme r) ])

        Stack.vstack (headerNode theme selected h :: rows)

    /// Render the diff. `width` is the available content width (used by `Split` to
    /// size its two columns); `selected` is the highlighted hunk index.
    let render
        (theme: AppTheme)
        (mode: DiffMode)
        (width: int)
        (selected: int)
        (hunks: DiffHunk list)
        : LayoutNode<'msg> =
        hunks
        |> List.mapi (fun i h ->
            let sel = i = selected

            match mode with
            | Unified -> unifiedHunk theme sel h
            | Split -> splitHunk theme width sel h)
        |> Stack.vstack
