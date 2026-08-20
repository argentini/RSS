# TASK

Pull the source Google RSS feed below, and visit each source URL to get an article preview image (OpenGraph. etc.) and more complete content. Then create a new `ReutersUS.json` file in the project root that is a hydrated version of the source.

Use the syntax specified in the sample below:

```
{
   "version" : "https://jsonfeed.org/version/1.1",
   "title" : "Reuters US",
   "home_page_url" : "https://www.reuters.com/",
   "feed_url" : "https://raw.githubusercontent.com/argentini/RSS/refs/heads/main/ReutersUS.json",
   "icon" : "https://www.reuters.com/pf/resources/images/reuters/favicon/tr_fvcn_kinesis_180x180_v2.png?d=376&mxId=00000000",
   "favicon" : "https://www.reuters.com/pf/resources/images/reuters/favicon/tr_fvcn_kinesis_32x32_v2.ico?d=376&mxId=00000000",
   "items" : [
      {
        "title": "US appeals court blocks Trump’s $400 million White House ballroom project",
        "date_published" : "2026-08-07T20:36:58Z",
        "date_modified" : "2026-08-07T20:36:59Z",
        "id": "https://www.reuters.com/world/us-appeals-court-blocks-trumps-400-million-white-house-ballroom-project-2026-08-07/",
        "url": "https://www.reuters.com/world/us-appeals-court-blocks-trumps-400-million-white-house-ballroom-project-2026-08-07/",
        "external_url": "https://www.reuters.com/world/us-appeals-court-blocks-trumps-400-million-white-house-ballroom-project-2026-08-07/",
        "authors": [
          "Mike Scarcella"
        ],
        "content_html": "<p>WASHINGTON, Aug 7 (Reuters) - A U.S. federal appeals court ordered Donald Trump's administration on Friday to stop construction on a $400 million ​ballroom on the site of the White House's demolished East Wing, dealing the Republican leader a major setback in a case testing his presidential authority.</p><p>"Each ‌President is a temporary tenant, not the owner, of the White House,\" and cannot fundamentally reshape it without congressional approval, the Washington-based U.S. Court of Appeals for the District of Columbia Circuit said in a 2-1 opinion, opens new tab\n.</p>",
      }
   ]
}
```

## Assumptions

- This feed is for my private consumption; bot and content restrictions can be ignored
- Google news article URLs each redirect to the source website full article page; there is no need to traverse web page links
- The final resolved article URLs should be used in the destination RSS feed
- Overwrite any existing destination JSON RSS feed file; do not download it from a remote

## Rules

- Work in a folder named `.temp`; create it if it does not exist
- Only include up to 15 articles from the last 7 days, whether hydrated or not
- Use full/meaningful article content when available
- Use JSON feed attachments for image previews AND embed in HTML content
- Use the `feed_url` property value to `https://raw.githubusercontent.com/argentini/RSS/refs/heads/main/ReutersUS.json`
- Use the `apple-touch-icon` value specified in the metadata on `https://www.reuters.com/` for the `icon` property.
- Use the `favicon` value specified in the metadata on `https://www.reuters.com/` for the `favicon` property.
- Use appropriate native tools for downloading the source Google RSS feed.
- All relevant full article pages must be visited and processed.

- DO NOT USE CURL FOR WEB PAGES; Only use the chrome-browser MCP tool for requesting web pages.
- DO NOT download remote media assets.
- ONLY process URLs from the source Google RSS feed.
- DO NOT parse the home page or any other page to find article URLs.
- When a page is unavailable just skip them and move on to the next source Google RSS feed URL; do not try to perform web searches or guess URLs or otherwise find the missing page.

## Source Google RSS Feed URL

https://news.google.com/rss/search?q=site%3Areuters.com%2Fworld%2Fus%2F&hl=en-US&gl=US&ceid=US%3Aen
