# TASK

Scan the source website URL below and scrape the markup for blog/news posts. Create/update an RSS file with new post additions and store it in the folder path under my home folder "Developer/RSS/" (create the file and/or folder if either does not exist).

You will be:
- Adding new posts based on the rules below
- Skipping new posts based on the rules below
- Removing posts from the existing file based on the rules below
- Pruning the RSS file based on the maximum retention age

The folder is a git repository. Commit the changes with message "Updated on {yyyy-mm-dd}" where {yyyy-mm-dd} is the current date, and push the current branch to origin.

## GIT REPO RULES

ONLY commit and push the updated file if ALL of the rules below are true:

- No new posts were added
- No existing posts were removed
- No new posts were skipped
- The file was not pruned based on maximum retention age
- Last build date is not the only change to the file

OTHERWISE revert/undo any file changes so the git repo is not affected.

## VALID POST RULES

- Only include posts that are not in the existing file (identified by date and headline) with dates at or newer than the maximum post age.
- Skip new posts older than the maximum retention age.
- Skip new subscriber-only posts.
- Skip new paywalled posts.

## POST CLEANUP RULES

- Remove existing posts older than the maximum retention age.
- Remove existing subscriber-only posts.
- Remove existing paywalled posts.

## SCRAPING RULES

- Use the original source for each post; the feed link should be the same as the source URL.
- Include post preview images when available.
- Ignore available preview length.
- Treat access markers as restricted.
- Never include restricted teasers.
- DO NOT prefer any existing RSS feed when present.
- Always mask as a human site visitor using the latest version of Google Chrome (user agent).
- When a post refers/points/links to a full original post on the same website, you should use the full original post content for the feed.
- Evaluate every visible post card, including posts below featured sections.
- Follow listing pagination when present but only through the maximum post age.

## CONFIGURATION

- Source URL: https://...
- RSS file name: filename.rss
- Maximum post age: 30 days
- Maximum retention age: 1 year