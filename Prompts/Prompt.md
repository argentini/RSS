# TASK

For each configured website in WEBSITES, scan that website's {Source URL} and build or replace that website's {RSS file}.

Process each configured website independently.

For the current website, treat the website's configured values as:

- {Website name}
- {Source URL}
- {RSS file}
- {RSS path}
- {Temporary workspace}

Find blog, news, or article posts from {Source URL}. Use website markup, metadata, and visible page content to identify valid posts.

For each valid post:

- Find the original source URL.
- Follow the original source URL only when it leads to a full post page on the same website.
- Extract full accessible article content from the original post page only when it passes content-quality checks.
- Otherwise retain substantial usable listing-page content or omit the post.
- Preserve article wording and useful HTML. Do not summarize, shorten, rewrite, or add commentary.
- Include a preview image when available.
- Use the canonical original source URL as the RSS item link and guid.

Generate a candidate RSS file in {Temporary workspace}.

Do not replace {RSS path} until validation passes.

{Project folder} is a Git repository.

After successful validation for the current website:

- Replace that website's {RSS path}.
- Defer commit and push until all configured websites have been processed.

Do not modify tracked files other than configured RSS files.

If one website fails extraction or validation:

- Do not replace that website's {RSS path}.
- Do not commit broken output for that website.
- Keep that website's {Temporary workspace} for diagnosis.
- Continue processing the remaining configured websites when repository state is still safe.
- Report the concise failure reason for that website.

# REFERENCE RULE

After CONFIGURATION, always refer to configured values by their configuration labels rather than repeating literal paths, filenames, URLs, dates, or limits.

Examples:

- Use `{Temporary workspace}`, not its literal directory.
- Use `{RSS file}`, not its literal filename.
- Use `{Project folder}`, not its literal repository path.
- Use `{Website name}`, not a repeated literal website label.
- Use `{Source URL}`, not its literal URL.
- Use `{Maximum post age}`, not its literal duration.
- Use `{Current UTC timestamp}`, not an inline timestamp calculation.

Treat configuration labels as authoritative variables.

When processing a website from WEBSITES, references to {Source URL}, {RSS file}, {RSS path}, and {Temporary workspace} mean the values for the current website only.

# WORKSPACE RULES

Create temporary files only inside the current website's {Temporary workspace}.

Use {Temporary workspace} for:

- raw listing HTML
- raw article HTML
- rendered DOM HTML
- screenshots
- candidate post data
- normalized post data
- validation output
- selector notes
- tool logs
- browser profile
- temporary scripts

Do not commit temporary files.

Never stage or commit {Temporary workspace}.

Remove a website's {Temporary workspace} only after the final successful push, and only when it is untracked and safe to delete.

Do not replace {RSS path} until validation passes.

If extraction or validation fails for the current website:

- Do not replace {RSS path}.
- Do not commit broken output.
- Keep {Temporary workspace} for diagnosis.
- Report the concise failure reason.
- Continue with the next configured website when possible.

# EXECUTION STRATEGY

Prefer deterministic local tools and scripts over manual reasoning.

Use an existing local feed-builder or validation script when available.

If no suitable script exists, create a temporary script only inside {Temporary workspace}. Do not add scraper logic, selectors, or generated files to tracked project files.

Use local command-line tools for repeatable work:

- Always fetch page markup with built-in browser. I don't want you to record the screen or call an external web browser. I want you to use the built-in browser to get web page markup.
- `htmlq` for HTML extraction and link discovery.
- `jq` for JSON, JSON-LD, and API responses.
- `xmllint` for XML validation.
- `tidy-html5` only when malformed HTML blocks reliable parsing.
- Playwright only when static responses are incomplete or JavaScript-rendered.
- Git commands for repository state, staging, commit, and push.

Do not use recursive crawling by default.

Discover relevant links first.

Fetch only necessary listing pages and article pages. 
When presented with buttons, links, or options to load more content, activate that feature to traverse pages of results and index until the post dates exceed the maximum post age rule.

Use low concurrency, bounded retries, delays, and timeouts.

Use a descriptive User-Agent for the feed generator.

Keep requests inside the domain of the current website's {Source URL} unless explicitly required otherwise.

Use a maximum 30-second browser navigation timeout per page.

Use a shorter fallback timeout for network-idle waits.

Abort and reject a page when rendering does not complete within the configured timeout.

# FETCH DECISION TREE

For every listing page and article page:

1. Fetch page markup with built-in browser. I don't want you to record the screen or call an external web browser. I want you to use the built-in browser to get web page markup.
    - Open {Source URL} visibly.
    - Wait for article cards.
    - Save rendered DOM.
    - Extract from rendered DOM.
    - Fetch article pages in browser.
    - Save each rendered article DOM.
    - Use browser cookies/session.
    - Do not bypass CAPTCHA.
2. Save the raw response inside {Temporary workspace}.
3. Check whether the response contains usable markup and does not contain challenge or access-control indicators.
4. Extract from the static response when it contains usable article or listing content.
5. Use Playwright only when the static response is a JavaScript shell, incomplete, challenged, or lacks usable article markup.
6. Save rendered DOM HTML inside {Temporary workspace}.
7. Extract from rendered DOM only, never screenshot OCR.
8. Reject rendered pages that are challenged, blocked, inaccessible, or lack meaningful article content.
9. Prefer same-origin structured page data when it agrees with visible page content.
10. Save screenshots only when extraction fails or challenge text is detected.

Use a persistent Chromium profile only at:

`{Temporary workspace}/browser-profile/`

When using Playwright:

- Navigate normally.
- Wait for visible article or listing content.
- Save rendered DOM HTML inside `{Temporary workspace}/rendered/`.
- Inspect same-origin network responses for JSON, JSON-LD, GraphQL, or content APIs.
- Prefer structured data only when it matches visible content.
- Do not bypass CAPTCHA, verification, login, subscription, payment, or other access controls.

# DISCOVERY RULES

Infer website structure from fetched HTML and rendered DOM.

The configured article markup hint is a clue, not a complete extraction contract.

Use semantic evidence in this priority order:

1. JSON-LD metadata.
2. Canonical URLs.
3. Open Graph metadata.
4. `<article>` markup.
5. `<time>` elements.
6. Headings.
7. Repeated listing-card structure.
8. Site-specific classes and data attributes.

Save raw HTML before extraction.

Record discovered extraction methods in:

`{Temporary workspace}/selectors.json`

Do not save site-specific scraper rules in tracked project files.

When an extraction method fails:

- Inspect saved HTML or rendered DOM.
- Try a different local extraction method.
- Do not guess missing content.

Inspect every visible candidate post on each relevant listing page, including posts below featured sections.

Follow listing pagination when present.

Stop following pagination once all remaining dated posts are older than {Maximum post age}.

# POST VALIDITY RULES

A valid post must have:

- Headline.
- Publication date.
- Usable original source URL.
- Non-empty article content.

A post may also include:

- Author byline.
- Preview image.
- Summary text.
- Categories.

Reject posts that are:

- Older than {Maximum post age}.
- Written in a language other than English.
- Subscriber-only.
- Paywalled.
- Login-required.
- Preview-only when no full original article exists.
- Missing title, date, source URL, or usable content.

Treat posts as duplicates when normalized date and normalized headline match.

Normalize headlines by:

- Decoding HTML entities.
- Trimming whitespace.
- Collapsing repeated whitespace.

Normalize URLs by:

- Resolving relative URLs.
- Removing fragments.
- Removing common tracking parameters.
- Preferring canonical URLs when available.

# EXTRACTION CONFIDENCE

Do not accept a candidate based on one weak signal.

Accept a post only when at least two independent signals support its identity.

Examples:

- JSON-LD title plus visible heading.
- Canonical URL plus article heading.
- `<time>` element plus metadata date.
- Listing-card URL plus article-page title.

For every accepted post, record confidence for:

- Title.
- Date.
- URL.
- Full content.
- Image.
- Paywall status.

Store candidate and confidence data in:

`{Temporary workspace}/posts.json`

When title, date, URL, or full body cannot be extracted with enough confidence:

- Omit the post.
- Do not invent data.
- Do not use unrelated surrounding text.

# ARTICLE CONTENT RULES

When a candidate links to a full original article on the same website:

1. Extract usable listing-page content first.
2. Fetch the full article page.
3. Use full-page content only when it is verified as a real accessible article page and passes all content-quality checks.
4. Keep usable listing-page content when the full-page content is rejected.
5. Omit the post when neither source has usable content.

Reject a full article page when it contains any of:

- Enable JavaScript and cookies to continue
- Just a moment...
- Checking your browser
- Verify you are human
- captcha
- cf-chl
- challenge-platform
- Access denied
- Request blocked
- unusual traffic
- Login
- subscription
- payment-wall

Also reject a page when:

- It lacks a meaningful article heading.
- It lacks meaningful article body markup.
- Its visible article body is too short.
- Its body mostly matches generic site boilerplate.
- Its title does not reasonably match the candidate title.
- It is primarily navigation, promotion, comments, forms, or unrelated text.

Never include rejected-page text in RSS output.

Preserve useful article HTML where possible:

- Decode entity-encoded HTML tags before sanitizing and writing RSS output.
- Paragraphs.
- Headings.
- Lists.
- Quotes.
- Tables.
- Images.
- Captions.
- Links.
- Code blocks.

Remove:

- Navigation.
- Site headers.
- Site footers.
- Ads.
- Subscription prompts.
- Paywall notices.
- Challenge pages.
- Login prompts.
- Cookie walls.
- Related-post sections.
- Comment sections.
- Social-sharing widgets.
- Forms.
- Scripts.
- Styles.
- Tracking pixels.
- Repeated boilerplate.

# CONTENT QUALITY RULES

Reject an extracted post body when any condition is true:

- It contains anti-bot, CAPTCHA, verification, login, subscription, payment-wall, or cookie-wall text.
- It contains fewer than 200 meaningful characters after stripping HTML.
- It lacks normal article-content signals such as multiple paragraphs, article markup, or structured article metadata.
- It substantially matches a generic challenge or access-control template.
- Its extracted title does not reasonably match the candidate title.
- It is mostly navigation, promotion, comments, or boilerplate.

Prefer full-post content only when it passes these checks.

# RSS OUTPUT RULES

Generate valid RSS 2.0 XML.

Use UTF-8.

Include namespaces:

```xml
xmlns:content="http://purl.org/rss/1.0/modules/content/"
xmlns:media="http://search.yahoo.com/mrss/"
```

Include channel fields:

- `title`
- `link`
- `description`
- `lastBuildDate`

Create one `<item>` for each valid unique post.

Every item must include:

- `title`
- `link`
- `guid`
- `pubDate`
- `description`
- `content:encoded`

Use the canonical source URL for:

- `link`
- `guid`

Set:

```xml
<guid isPermaLink="true">
```

Use RFC 822 UTC dates for:

- `pubDate`
- `lastBuildDate`

Use CDATA for HTML in:

- `description`
- `content:encoded`

Inside CDATA, write sanitized HTML as real tags, not XML-escaped tag text.

Examples:

- Use `<a href="...">text</a>`, not `&lt;a href="..."&gt;text&lt;/a&gt;`.
- Use `<br>`, not `&lt;br&gt;`.
- Use `<div><img ...></div>`, not `&lt;div&gt;&lt;img ...&gt;&lt;/div&gt;`.

Do not double-escape HTML. Decode entity-encoded allowed tags before wrapping them in CDATA.

Only protect the CDATA terminator sequence if it appears in article content.

Use `description` for a safe HTML preview or article opening.

Use `content:encoded` for full sanitized article HTML.

Add `media:content` only when a valid preview-image URL exists.

Sort items newest first.

Include only posts within {Maximum post age}.

Do not remove valid existing feed items unless they are:

- Older than {Maximum post age}.
- Positively identified as invalid.
- Duplicates of newer normalized items.

If the current run finds fewer valid posts than the existing feed, preserve existing in-range items unless they are positively identified as invalid, duplicate, or older than {Maximum post age}.

# VALIDATION RULES

Before replacing {RSS path}:

1. Confirm XML is well-formed with `xmllint`.
2. Confirm the RSS root and required namespaces exist.
3. Confirm every item has:
   - `title`
   - `link`
   - `guid`
   - `pubDate`
   - non-empty `description`
   - non-empty `content:encoded`
4. Confirm every item link belongs to the current website's {Source URL} domain.
5. Confirm no duplicate normalized date-plus-headline pairs exist.
6. Confirm no item exceeds {Maximum post age}.
7. Confirm publication dates parse successfully.
8. Confirm the feed has at least one valid item.
9. Confirm no obvious navigation, ad, or paywall text appears in article bodies.
10. Confirm no article body contains challenge, CAPTCHA, login, payment-wall, cookie-wall, or browser-verification text.
11. Confirm `description` and `content:encoded` do not contain entity-encoded HTML tag markers such as `&lt;a`, `&lt;br`, `&lt;div`, `&lt;p`, `&lt;img`, `&lt;strong`, `&lt;/`, or `&gt;`.
12. Confirm every article body contains at least 200 meaningful characters after stripping HTML.
13. Save validation results to:

`{Temporary workspace}/validation.json`

If validation fails:

- Do not replace {RSS path}.
- Do not commit.
- Do not push.
- Preserve {Temporary workspace}.
- Report the concise failure reason.

# GIT RULES

Before changing {RSS file}:

1. Run `git status --short` from {Project folder}.
2. Ignore untracked files.
3. Confirm there are no tracked changes outside configured RSS files.
4. Confirm any configured RSS files already changed are files changed earlier in this run.

After validation succeeds:

1. Replace {RSS path}.
2. Run `git diff --check`.
3. Run `git status --short`.
4. Do not stage, commit, push, or remove {Temporary workspace} yet.

After all configured websites have been processed:

1. Run `git diff --check`.
2. Run `git status --short`.
3. Stage only configured RSS files that changed during this run.
4. Confirm the staged diff contains only configured RSS files changed during this run.
5. Commit only when at least one staged configured RSS file changed.
6. Use commit message:

`Updated on {Current UTC timestamp}`

7. Push the current branch to origin.
8. After a successful push, remove successful websites' {Temporary workspace} directories only when they are untracked and safe to delete.

If no configured RSS file changed:

- Do not create an empty commit.
- Do not push a new commit.
- Remove successful websites' {Temporary workspace} directories only when they are untracked and safe to delete.

If final staging, commit, or push fails:

- Do not remove any {Temporary workspace}.
- Report the concise failure reason.

# MULTI-WEBSITE RULES

Process websites in the order listed in WEBSITES.

For each website:

1. Resolve {RSS path} from {Project folder} and that website's {RSS file}.
2. Use only that website's {Source URL} for discovery and same-domain article fetches.
3. Use only that website's {Temporary workspace} for temporary files.
4. Generate and validate that website's candidate RSS file independently.
5. Replace only that website's {RSS file} after validation succeeds.

Do not let selectors, temporary scripts, rendered pages, candidate data, validation data, or browser profiles from one website affect another website.

Do not combine posts from different websites into one RSS file.

Do not use one website's {Source URL} as the source-domain validator for another website's RSS items.

If repository status shows tracked changes outside configured RSS files before processing starts:

- Stop before modifying any RSS file.
- Report the unsafe tracked files.

If repository status shows tracked changes outside configured RSS files after a website has been processed:

- Do not stage, commit, or push yet.
- Preserve that website's {Temporary workspace}.
- Continue only when the unrelated tracked changes are configured RSS files already changed by this run.

# OUTPUT

Return one concise result block per configured website, and then a total summary.

For each website, return only:

- {Website name}.
- Valid item count.
- Added count.
- Updated count.
- Preserved count.
- Rejected count.
- Whether {RSS file} changed.
- Concise failure reason when unsuccessful.
- {Temporary workspace} location when unsuccessful.

For the total summary, return only:

- Websites processed count.
- Websites succeeded count.
- Websites failed count.
- RSS files changed count.
- Commit hashes and push results when applicable.

Do not paste raw HTML, article bodies, command output, screenshots, or intermediate reasoning unless necessary to explain a failure.

# CONFIGURATION

- {Project folder}: `~/Developer/RSS/`
- {RSS path}: `{Project folder}/{RSS file}`
- {Maximum post age}: `7 days`
- {Current-date timezone}: `UTC`
- {Current UTC timestamp}: current timestamp in `{Current-date timezone}`, formatted as `yyyy-mm-dd HH:MM:SS UTC`
- {Temporary workspace root}: `.tmp/rss-build`

# WEBSITES

- {Website name}: `AP News`
  - {Source URL}: `https://apnews.com/hub/latest-news/`
  - {RSS file}: `APNews.rss`
  - {Temporary workspace}: `{Temporary workspace root}/apnews/`
- {Website name}: `AppleInsider`
  - {Source URL}: `https://appleinsider.com/`
  - {RSS file}: `AppleInsider.rss`
  - {Temporary workspace}: `{Temporary workspace root}/appleinsider/`
- {Website name}: `Ihnatko`
  - {Source URL}: `https://ihnatko.com/`
  - {RSS file}: `Ihnatko.rss`
  - {Temporary workspace}: `{Temporary workspace root}/ihnatko/`
- {Website name}: `ProPublica`
  - {Source URL}: `https://www.propublica.org/`
  - {RSS file}: `Propublica.rss`
  - {Temporary workspace}: `{Temporary workspace root}/propublica/`
- {Website name}: `Reuters US`
  - {Source URL}: `https://www.reuters.com/world/us/`
  - {RSS file}: `ReutersUS.rss`
  - {Temporary workspace}: `{Temporary workspace root}/reutersus/`
- {Website name}: `Umbraco Blog`
  - {Source URL}: `https://umbraco.com/blog/`
  - {RSS file}: `UmbracoBlog.rss`
  - {Temporary workspace}: `{Temporary workspace root}/umbracoblog/`