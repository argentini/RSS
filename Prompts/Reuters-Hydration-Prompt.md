/caveman

# TASK

Overwrite the existing destination RSS file with the latest 15 available articles from the source Google RSS feed. The destination RSS should be hydrated with better content and image previews, and the article URLs should be replaced with the source article URLs (resolved from the Google RSS feed URLs which redirect to the source article URLs).

The destination feed is for my private consumption; bot and content restrictions should be ignored.

## Destination RSS File

The destination file is `ReutersUS.json` in the project root. If it exists, replace it.

## Source Google RSS Feed URL

https://news.google.com/rss/search?q=site%3Areuters.com%2Fworld%2Fus%2F&hl=en-US&gl=US&ceid=US%3Aen

## Expected RSS JSON Syntax

Use the RSS JSON syntax specified in the sample below:

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

# TASK STEPS

Follow the steps below in order:

## STEP 1

If a project folder path named `.temp/reu` does not exist, create it. Delete all content in the `.temp/reu` folder to prepare for the next step. This folder is the *working directory*.

## STEP 2

Use appropriate native tools for downloading the source Google RSS feed and save in the working directory.

## STEP 3

Only using the chrome-browser MCP tool (never curl or other CLI tools) visit the latest 15 article URLs which resolve to original article URLs before loading the pages. When a URL is bad, skip it and try the next.

Use browser download trick (Blob → <a download>) to get page content.

Save the DOM-rendered HTML source for each article to its own file in the working directory. DO NOT download remote media assets.

Keep going until you have 15. You should always have 15 good original article HTML files.

NEVER use curl or other CLI tools to retrieve web pages. If the chrome-browser MCP tool is unresponsive ask me to start it.

## STEP 4

Parse the first file to hydrate the metadata for the destination RSS:

- Use the `feed_url` property value to `https://raw.githubusercontent.com/argentini/RSS/refs/heads/main/ReutersUS.json`
- Use the `apple-touch-icon` value specified in the metadata on `https://www.reuters.com/` for the `icon` property.
- Use the `favicon` value specified in the metadata on `https://www.reuters.com/` for the `favicon` property.

## STEP 5

Loop through all the files to generate the destination RSS file using the appropriate Google RSS feed entry as a base and hydrating from the HTML file.

### Rules

- Do not traverse web page links or look for web pages or URLs.
- The final resolved source article URLs should be used in the destination RSS feed
- Identify and use full/meaningful article content when available
- Use the first source article image as the preview
- When available use the first source article image, or when not available use JSON feed attachments, for image previews AND embed at the top of HTML content.
