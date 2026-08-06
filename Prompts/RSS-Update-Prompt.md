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

**IMPORTANT:** If a site (e.g. Reuters) uses an anti-bot service like DataDome on a listing page, use Safari normally and inspect the markup and visible content Safari can access. Do not bypass CAPTCHA or other access controls.

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

After successful validation for the current website, replace that website's {RSS path}.

Do not modify files other than configured RSS files and files inside the current website's {Temporary workspace}.

If one website fails extraction or validation:

- Do not replace that website's {RSS path}.
- Keep that website's {Temporary workspace} for diagnosis.
- Continue processing the remaining configured websites.
- Report the concise failure reason for that website.

# REFERENCE RULE

After CONFIGURATION, always refer to configured values by their configuration labels rather than repeating literal paths, filenames, URLs, dates, or limits.

Examples:

- Use `{Temporary workspace}`, not its literal directory.
- Use `{RSS file}`, not its literal filename.
- Use `{RSS folder}`, not its literal folder path.
- Use `{Website name}`, not a repeated literal website label.
- Use `{Source URL}`, not its literal URL.
- Use `{Maximum post age}`, not its literal duration.
- Use `{Current UTC timestamp}`, not an inline timestamp calculation.

Treat configuration labels as authoritative variables.

When processing a website from WEBSITES, references to {Source URL}, {RSS file}, {RSS path}, and {Temporary workspace} mean the values for the current website only.

# WORKSPACE RULES

Create temporary files only inside the current website's {Temporary workspace}.

Use {Temporary workspace} for:

- Safari-provided listing HTML and rendered DOM
- Safari-provided article HTML and rendered DOM
- screenshots needed for diagnosis
- candidate post data
- normalized post data
- validation output
- selector notes
- tool logs

Keep each website's temporary data isolated from every other website.

Do not replace {RSS path} until validation passes.

Keep a failed website's {Temporary workspace} for diagnosis.

After all websites have been processed, remove each successful website's {Temporary workspace}. Do not remove a failed website's {Temporary workspace}.

# EXECUTION STRATEGY

The tool directly communicates with Safari. Use Safari for all website navigation, page loading, interaction, markup access, DOM inspection, metadata access, and website-content parsing.

If possible, open a new dedicated Safari window for this task before processing the first website. Use only that window for this task. Do not reuse, close, rearrange, or alter the user's existing Safari windows or tabs. If Safari cannot create a separate window, open a dedicated new tab and avoid disturbing existing tabs.

Website access and website-content parsing must occur exclusively through Safari. Use file operations and in-process data handling only for post normalization, RSS generation, and RSS validation.

Do not use recursive crawling by default.

Discover relevant links first.

Visit only necessary listing pages and article pages.

When presented with buttons, links, or options to load more content, activate that feature in Safari to traverse results until the post dates exceed {Maximum post age}.

Use low concurrency, bounded retries, delays, and timeouts.

Keep navigation inside the domain of the current website's {Source URL} unless explicitly required otherwise.

Use a maximum 30-second Safari navigation timeout per page.

Abort and reject a page when meaningful content does not finish rendering within the configured timeout.

# SAFARI PAGE WORKFLOW

For every listing page and article page:

1. Navigate normally in the dedicated Safari window.
2. Wait for meaningful listing or article content to render.
3. Use the page markup, rendered DOM, visible content, metadata, and structured data available directly through Safari.
4. Preserve the normal Safari cookies and session for the dedicated window.
5. Save the relevant rendered DOM or markup inside {Temporary workspace}.
6. Extract from Safari-provided markup and rendered DOM only. Never use screenshot OCR.
7. Inspect same-origin JSON-LD and other structured page data when Safari exposes it, but use it only when it agrees with visible content.
8. Reject pages that are challenged, blocked, inaccessible, or lack meaningful article content.
9. Do not bypass CAPTCHA, verification, login, subscription, payment, or other access controls.
10. Save screenshots only when extraction fails or challenge text is detected.

When all configured websites have been processed, close only the dedicated Safari window or tab created for this task, when possible. Do not close or modify any pre-existing Safari window or tab.

# DISCOVERY RULES

Infer website structure from Safari-provided HTML and rendered DOM.

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

Save the Safari-provided HTML or rendered DOM before extraction.

Record discovered extraction methods in:

`{Temporary workspace}/selectors.json`

Do not save site-specific scraper rules outside the current website's {Temporary workspace}.

When an extraction method fails:

- Inspect saved HTML or rendered DOM.
- Try a different Safari-based extraction method.
- Do not guess missing content.

Inspect every visible candidate post on each relevant listing page, including posts below featured sections.

Follow listing pagination when present.

Stop following pagination once all remaining dated posts are older than {Maximum post age}.

For Reuters-style Arc/Fusion pages:
- Extract visible listing cards from rendered DOM links matching `a[href*="2026-"]`.
- Use the closest story/card container for headline, summary, image, and relative timestamp.
- Parse relative timestamps such as `2 hours ago` against {Current UTC timestamp}.
- Open each article page in Safari and prefer JSON-LD `NewsArticle` metadata plus visible paragraph nodes such as `[data-testid="paragraph"]`, `article p`, or `main p`.

# HARD FRESHNESS GATE

Before preserving existing RSS items, collect all visible same-domain article candidates from {Source URL} rendered DOM.

For every visible candidate newer than the newest generated RSS item:

- Extract it into the feed, or
- Record a concrete rejection reason in {Temporary workspace}/posts.json.

Validation must fail when:

- The feed contains only preserved existing items, and
- The rendered listing contains newer valid-looking article candidates.

Do not treat preserved items as proof of success.

If freshness validation fails:

- Do not replace {RSS path}.
- Keep {Temporary workspace}.
- Report the newest skipped candidate and rejection reason.

For Reuters, parse listing links matching `a[href*="2026-"]`.
Use closest story/card container.
Parse relative timestamps like `3 days ago`.
Open each article page in Safari.
Prefer JSON-LD plus visible article paragraphs.

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
2. Open the full article page in Safari.
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

1. Confirm XML is well-formed using an in-process XML parser.
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

14. Confirm listing freshness coverage:
    - From the rendered listing DOM, collect all visible same-domain article links that appear to be posts.
    - Treat dated URL slugs like `/2026-07-11/` and visible relative timestamps like `2 hours ago`, `Yesterday`, or `N days ago` as listing-date evidence.
    - If the rendered listing contains newer valid-looking article candidates than the newest generated RSS item, validation fails unless those candidates are explicitly recorded as rejected with a concrete content-quality reason.
    - A feed must not pass validation when all or nearly all items are preserved existing items and the rendered listing contains newer candidates.

If validation fails:

- Do not replace {RSS path}.
- Preserve {Temporary workspace}.
- Report the concise failure reason.

# RSS FILE UPDATE RULES

Before changing {RSS file}:

1. Read the existing {RSS path} when it exists.
2. Generate the complete candidate RSS file inside {Temporary workspace}.
3. Validate the candidate independently from the existing file.
4. Do not change {RSS path} while extraction or validation is incomplete.

After validation succeeds:

1. Compare the validated candidate with the existing {RSS path}, when it exists.
2. If the content differs, replace only the current website's {RSS path} using an atomic replacement so a partial write cannot damage {RSS file}.
3. If the content is identical, leave {RSS path} unchanged.
4. When replacement occurs, confirm the replaced {RSS file} can be reopened and parsed successfully.
5. Record whether {RSS file} changed.

If no configured RSS file changed, make no other filesystem changes except removal of successful websites' temporary workspaces.

# MULTI-WEBSITE RULES

Process websites in the order listed in WEBSITES.

For each website:

1. Resolve {RSS path} from {RSS folder} and that website's {RSS file}.
2. Use only that website's {Source URL} for discovery and same-domain article visits.
3. Use only that website's {Temporary workspace} for temporary files.
4. Generate and validate that website's candidate RSS file independently.
5. Replace only that website's {RSS file} after validation succeeds.

Do not let selectors, rendered pages, candidate data, validation data, Safari tabs, or Safari page state from one website affect another website.

Do not combine posts from different websites into one RSS file.

Do not use one website's {Source URL} as the source-domain validator for another website's RSS items.

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

Do not paste raw HTML, article bodies, screenshots, or intermediate reasoning unless necessary to explain a failure.

# CONFIGURATION

- {RSS folder}: `~/Developer/RSS/`
- {RSS path}: `{RSS folder}/{RSS file}`
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