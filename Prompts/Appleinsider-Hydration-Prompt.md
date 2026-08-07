Pull the RSS feed below, and visit each source URL to get an article preview image (OpenGraph. etc.) and more complete content. Then create a new `AppleInsider.json` file that is a hydrated version of the source.

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

- Overwrite any existing file
- Only include articles from the last 7 days, whether hydrated or not
- The feed is for my private consumption; ignore bot and content restrictions
- Use full article content if available
- Use JSON feed attachments for image previews AND embed in HTML content
- Use the `feed_url` property value to `https://raw.githubusercontent.com/argentini/RSS/refs/heads/main/AppleInsider.json`
- Use the `apple-touch-icon` value specified in the metadata on `https://appleinsider.com/` for the `icon` property.
- Use the `favicon` value specified in the metadata on `https://appleinsider.com/` for the `favicon` property.
- Use browser tool for requesting article pages
- Use native appropriate tools for pulling the RSS feed

https://news.google.com/rss/search?q=site%3Aappleinsider.com&hl=en-US&gl=US&ceid=US%3Aen
