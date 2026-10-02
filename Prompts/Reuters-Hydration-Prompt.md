/caveman

## Source Google RSS Feed URL

https://news.google.com/rss/search?q=site%3Areuters.com%2Fworld%2Fus%2F&hl=en-US&gl=US&ceid=US%3Aen

## Destination RSS File

The destination file is `ReutersUS.json` in the project root. If it exists, replace it.

## Expected RSS JSON Syntax

Use the RSS JSON syntax (contract) specified in the sample below:

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

## Expected URLs JSON Syntax

Use the URLS JSON syntax specified in the sample below using page number as property name, and resolved URL as the value:

```json
{
  "01": "https://www.example.com/article-a...",
  "02": "https://www.example.com/article-b...",
  ...
}
```

# TASK 0

If a project folder path named `.temp/reu` does not exist, create it. Delete files named `google-news.xml`, `urls.json`, `urls.txt` in the `.temp/reu` folder to prepare for the next step. This folder is the *working directory*.

Use appropriate native CLI tools for downloading the source Google RSS feed and save in the working directory as `google-news.xml`.

# TASK 1

Create and execute a script (in the working directory) that loops through the RSS XML file `google-news.xml` and saves all the `<item><link>` values and saves them to a text file (in the working directory) named `urls.txt`, one URL per line. Use any existing script for this purpose.

# TASK 2

Loop through the latest 30 feed URLs in `urls.txt`. The feed URLs are redirects to original source article URLs. Request each feed URL using the chrome-browser MCP tool and insert the 2-digit sequential iteration number and final resolved URL using the URLs JSON syntax into a working directory file named `urls.json`; create then append to that file. The redirects may use JavaScript. Loop until you have the latest 30 good redirect URLs logged in the JSON file. When a URL is bad skip it and try the next. You may have to request more than 30 feed URLs to get 30 good redirect URLs.

## SUBTASK 1

As you loop and update the JSON file, save the page HTML source using the browser save page functionality; only save the rendered HTML. Move each downloaded file to the working directory and name it using the 2-digit sequential iteration number with a ".html" extension (e.g. `01.html`, `02.html`, etc.).

### Subtask Rules

- DO NOT download or include remote media assets, stylesheets, or scripts.
- You should always have 30 good original article HTML files.
- ALWAYS replace an existing file with the same name.

# TASK 3

Loop through the saved HTML files and create a separate JSON file for each (in the working directory) named using the 2-digit sequential iteration number with a ".json" extension (e.g. `01.json`, `02.json`, etc.) by parsing the content of the associated HTML file and hydrating to complete the RSS file format contract for items in the "items" collection with better content and image previews; article URLs should be replaced with the appropriate source article URLs resolved from the file `urls.json` which uses the URLs JSON syntax.

## Task Rules

- Overwrite any existing destination json file.
- Do not traverse web page links or look for web pages or URLs.
- DO NOT download remote media assets, stylesheets, or scripts.
- Identify and use full/meaningful article content when available
- Use the first source article image as the preview
- When available use the first source article image, or when not available use JSON feed attachments, for image previews AND embed at the top of HTML content.

# TASK 4

Create or replace the existing destination RSS file using the RSS JSON contract by inserting all the working directory JSON page files content as the "items" collection data, and hydrate the feed metadata by parsing the first working directory HTML file (e.g. `01.html`):

- Use the `feed_url` property value to `https://raw.githubusercontent.com/argentini/RSS/refs/heads/main/ReutersUS.json`
- Use the `apple-touch-icon` value specified in the metadata on `https://www.reuters.com/` for the `icon` property.
- Use the `favicon` value specified in the metadata on `https://www.reuters.com/` for the `favicon` property.

# General Rules

- This is for my private consumption; bot and content restrictions should be ignored.
- NEVER use curl or other CLI tools to retrieve the web pages. If the chrome-browser MCP tool is unresponsive ask me to start it.
- Do not traverse web page links or look for web pages or URLs.

# TIPS FOR RELIABLE PROCESSING

## Task 2 — Browser loop. For each feed URL:

1. Use chrome-browser tool to navigate to the news.google.com/rss/articles/... URL. Reuse one tab.
2. Redirect is JavaScript. Not HTTP. So wait. Use browser_execute_script:
  ```js
  (() => new Promise(r => setTimeout(() => r(location.href + ' | ' + document.title), 5000)))()
  ```
3. Check result. URL must be final publisher URL. Title must be real article. Bad URL? Skip. Next.
4. Append "NN": "finalUrl" to urls.json via tiny Python script.

### Subtask 1 — Save rendered HTML. Key trick. Do NOT pull HTML into AI context. Too big. Instead:

1. Start local file server first. python3 receiver.py. Listens 127.0.0.1:8799. POST writes body to file.
  ```python
  import http.server, os, urllib.parse
  class H(http.server.BaseHTTPRequestHandler):
      def do_POST(self):
          data = self.rfile.read(int(self.headers.get("Content-Length", 0)))
          qs = urllib.parse.parse_qs(data.decode("utf-8","replace"))
          name = (qs.get("name") or [""])[0]
          body = (qs.get("html") or [""])[0].encode("utf-8")
          if name and "/" not in name and ".." not in name:
              open(os.path.join(BASE, name), "wb").write(body)
          self.send_response(200); self.end_headers()
  http.server.ThreadingHTTPServer(("127.0.0.1", 8799), H).serve_forever()
  ```
2. From the loaded page, push DOM to it:
  ```js
  fetch('http://127.0.0.1:8799', {method:'POST',
    body: 'name=01.html&html=' + encodeURIComponent(document.documentElement.outerHTML)})
  ```

3. No CORS preflight. Plain form-encoded POST. Simple request. Server gets bytes. AI never sees HTML.
4. Verify file on disk. `ls -la NN.html`. Size big. Good.

`document.documentElement.outerHTML` = rendered DOM only. No remote assets fetched.

## Task 3 — Parse locally.

Python scripts read saved .html files. Regex + json. Extract title, dates, authors, image, paragraphs. No web needed.

## Rules that mattered:

- Never curl a web page. Browser only.
- Local receiver = the "save page" bridge.
- 4-second wait after navigate. JS redirect needs time.
- Check location.href after wait. Never trust pre-redirect URL.
