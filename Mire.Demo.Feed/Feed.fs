namespace Mire.Demo.Feed

open System
open System.Net.Http
open System.Xml.Linq
open System.Text.RegularExpressions
open Mire.Core

/// One feed entry, already reduced to plain text the TUI can wrap and render.
/// `SourceFeed` names the feed this article came from — populated when articles
/// are merged into the combined stream (empty as parsed).
type Article =
    { Title: string
      Link: string
      Date: string
      Summary: string
      Body: string
      SourceFeed: string }

/// Where a feed is in its load lifecycle.
type FeedStatus =
    | FLoading
    | FReady
    | FFailed of string

/// A subscribed feed: its URL is the identity (dedup key), `Name` is the parsed
/// channel title, `Articles` carry `SourceFeed = Name`.
type Feed =
    { Name: string
      Url: string
      AddedAt: System.DateTime
      Articles: Article list
      Status: FeedStatus }

module Feed =

    let private http =
        let c = new HttpClient()
        c.Timeout <- TimeSpan.FromSeconds 15.0
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mire.Demo.Feed/0.1")
        c

    // content:encoded lives in this RSS module namespace.
    let private contentNs = XNamespace.Get "http://purl.org/rss/1.0/modules/content/"

    let private valOf (el: XElement) =
        if isNull el then "" else el.Value.Trim()

    /// Reduce a snippet of HTML to wrap-friendly plain text: block elements become
    /// line breaks, list items get a bullet, remaining tags are stripped, entities
    /// decoded, and runs of blank lines collapsed. (No real HTML layout — that's a
    /// gap; the agent demo has a markdown-ish renderer but the framework has neither.)
    let htmlToText (html: string) : string =
        if String.IsNullOrWhiteSpace html then
            ""
        else
            let s = Regex.Replace(html, @"(?is)<\s*(br|/p|/div|/h[1-6]|/li|/tr)\s*/?\s*>", "\n")
            let s = Regex.Replace(s, @"(?is)<\s*li[^>]*>", "• ")
            let s = Regex.Replace(s, @"(?is)<\s*(pre|/pre|code|/code)[^>]*>", "")
            let s = Regex.Replace(s, @"(?s)<[^>]+>", "") // drop remaining tags
            let s = Net.WebUtility.HtmlDecode s
            let s = Regex.Replace(s, @"[ \t]+", " ")
            let s = Regex.Replace(s, @" *\n *", "\n")
            let s = Regex.Replace(s, @"\n{3,}", "\n\n")
            s.Trim()

    /// Parse an RSS 2.0 document into (feed title, articles).
    let parse (xml: string) : string * Article list =
        let doc = XDocument.Parse xml
        let channel = doc.Root.Element(XName.Get "channel")

        if isNull channel then
            "Feed", []
        else
            let feedTitle = valOf (channel.Element(XName.Get "title"))

            let items =
                channel.Elements(XName.Get "item")
                |> Seq.map (fun it ->
                    let summary = htmlToText (valOf (it.Element(XName.Get "description")))
                    let encoded = it.Element(contentNs + "encoded")
                    let body = if isNull encoded then summary else htmlToText encoded.Value

                    { Title = valOf (it.Element(XName.Get "title"))
                      Link = valOf (it.Element(XName.Get "link"))
                      Date = valOf (it.Element(XName.Get "pubDate"))
                      Summary = summary
                      Body = (if body = "" then summary else body)
                      SourceFeed = "" })
                |> Seq.toList

            (if feedTitle = "" then "Feed" else feedTitle), items

    let fetchAsync (url: string) : Async<Result<string * Article list, string>> =
        async {
            try
                let! xml = http.GetStringAsync(url) |> Async.AwaitTask
                return Ok(parse xml)
            with ex ->
                return Error ex.Message
        }

    /// Parse an RSS `pubDate` (RFC-1123 / RFC-822-ish) into a sortable instant.
    /// Unparseable dates sort oldest (`DateTime.MinValue`). Only used for ordering
    /// the combined stream — the displayed `Article.Date` string is left untouched.
    let parseDate (pubDate: string) : DateTime =
        match
            DateTimeOffset.TryParse(
                pubDate,
                Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.AssumeUniversal
                ||| Globalization.DateTimeStyles.AdjustToUniversal
            )
        with
        | true, dto -> dto.UtcDateTime
        | false, _ -> DateTime.MinValue

    /// True when `s` is a syntactically valid absolute http(s) URL with a host.
    /// The modal uses this for the instant "Invalid URL" check before fetching.
    let isValidUrl (s: string) : bool =
        match Uri.TryCreate((if isNull s then "" else s.Trim()), UriKind.Absolute) with
        | true, uri ->
            (uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps)
            && uri.Host <> ""
        | false, _ -> false

    /// Greedy word-wrap to `width` columns, grapheme-width aware, preserving the
    /// paragraph breaks already in the text. Over-long words are hard-broken.
    let wrap (width: int) (text: string) : string list =
        if width <= 1 then
            [ text ]
        else
            [ for para in text.Split('\n') do
                  if para = "" then
                      yield ""
                  else
                      let mutable cur = ""

                      for w in para.Split(' ') do
                          let candidate = if cur = "" then w else cur + " " + w

                          if Grapheme.stringWidth candidate <= width then
                              cur <- candidate
                          else
                              if cur <> "" then
                                  yield cur

                              if Grapheme.stringWidth w > width then
                                  let mutable rem = w

                                  while rem.Length > width do
                                      yield rem.Substring(0, width)
                                      rem <- rem.Substring(width)

                                  cur <- rem
                              else
                                  cur <- w

                      if cur <> "" then
                          yield cur ]
