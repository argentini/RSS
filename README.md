# RSS JSON Feed Generator

This repository contains a scheduled .NET console application that builds and publishes JSON Feed 1.1 files from Google News search feeds. The included configurations generate feeds for Reuters US and AppleInsider.

For each configured feed, the application:

1. Downloads the configured Google News RSS search.
2. Resolves the newest Google News links to their source articles.
3. Opens each article in Google Chrome through Agent360 Browser MCP and captures its rendered HTML.
4. Sends the cleaned HTML to a local LM Studio model, which extracts a structured JSON Feed item.
5. Merges new items with the existing feed while removing duplicates and preserving the configured feed depth.
6. Validates and atomically writes the JSON Feed file in the repository root.
7. Commits changed feed files and pushes the Git repository to its configured remote.

The process runs on the local schedule in `Generator/appsettings.json`. At startup, an interactive prompt also offers to run immediately.

## Requirements

- **macOS.** Automatic Chrome startup currently uses `/usr/bin/open`, so the application will reject other operating systems.
- **.NET 10 SDK.** Both the application and test project target `net10.0`.
- **Google Chrome.** Chrome renders source articles before their content is sent to the model.
- **Node.js and npm.** The application starts Agent360 with `npx --yes @agent360/browser-mcp@1.31.0`. The first run may download that package.
- **LM Studio.** A loaded model and a running local, OpenAI-compatible chat-completions server are required.
- **Git.** The repository must have a push remote and configured commit identity.
- **GitHub credentials.** A username and personal access token with permission to push this repository are required.
- **Internet access.** The application contacts Google News, configured article sites, npm when needed, and the Git remote.

## Setup

### 1. Clone the repository

```shell
git clone https://github.com/argentini/RSS.git
cd RSS
```

If needed, configure the identity used for generated commits:

```shell
git config user.name "Your Name"
git config user.email "you@example.com"
```

Confirm that `origin` points to a repository where your credentials can push:

```shell
git remote -v
```

### 2. Configure LM Studio

Install LM Studio, download and load the configured model, then start its local server. The default settings expect:

- Model: `qwen3.8-27b-mlx`
- Base URL: `http://127.0.0.1:1235`
- Endpoint: `POST /v1/chat/completions`
- Streaming responses enabled

Edit the `LMStudio` section in `Generator/appsettings.json` when using another model, port, or timeout:

```json
{
  "LMStudio": {
    "Model": "qwen3.8-27b-mlx",
    "BaseUrl": "http://127.0.0.1:1235",
    "UseReasoning": false,
    "TimeoutMinutes": 60
  }
}
```

`BaseUrl` must be an absolute loopback URL. Remote model servers are intentionally rejected.
`UseReasoning` sends `xhigh` when true and `none` when false. It defaults to true when omitted.

### 3. Add GitHub credentials

Create `Credentials.json` in the repository root:

```json
{
  "Github": {
    "UserName": "your-github-username",
    "PersonalAccessToken": "your-token"
  }
}
```

`Credentials.json` is ignored by Git. Never commit this file or share its token. The token must allow Git pushes to the configured repository.

#### `Credentials.json` specification

The file must be named exactly `Credentials.json` and placed directly in the repository root. Property names are case-sensitive.

| JSON path | Type | Required | Rules |
| --- | --- | --- | --- |
| `Github` | Object | Yes | Must contain the GitHub credential fields. |
| `Github.UserName` | String | Yes | Must contain a non-whitespace GitHub username. |
| `Github.PersonalAccessToken` | String | Yes | Must contain a non-whitespace token permitting pushes to this repository. |

The application reads this file during startup. Startup fails when the file is missing, empty, invalid JSON, missing the `Github` object, or contains blank required values. Environment variables are not supported as a replacement.

Credentials are supplied to the non-interactive `git push` process through a temporary credential helper restricted to `github.com`. They are not written into generated feeds. Keep the token out of terminal output, logs, issue reports, and committed files.

### 4. Review feed configurations

Feed definitions live in `Generator/Configurations`. Every top-level `.json` file in that directory is loaded at startup. Invalid files are skipped and reported.

Each configuration supports these fields:

| Field | Purpose |
| --- | --- |
| `GoogleNewsUrl` | Absolute Google News RSS search URL. |
| `LatestCount` | Number of newest articles to process. Must exceed zero. |
| `DestinationRssFilePath` | Output path. It must resolve directly into the repository root. |
| `FeedName` | JSON Feed title. |
| `FeedHomePageUrl` | Public home page for the source. |
| `FeedUrl` | Public URL of the generated feed. |
| `FeedIconUrl` | Absolute feed icon URL. |
| `FeedFaviconUrl` | Absolute feed favicon URL. |

To add another feed, copy an existing configuration, give it a unique filename, and update every field. Place its destination file directly in the repository root.

### 5. Review the schedule

The `Schedule` array in `Generator/appsettings.json` controls recurring runs. Times use the computer's local time zone and the 24-hour `HH:mm` format:

```json
{
  "DayOfWeek": "Monday",
  "TimeOfDay": "08:00"
}
```

At least one schedule item is required. Duplicate entries and invalid day or time values stop startup.

### 6. Restore, build, and test

```shell
dotnet restore Generator/Generator.csproj
dotnet build Generator/Generator.csproj
dotnet test Generator.Tests/Generator.Tests.csproj
```

## Run

Start LM Studio's local server, then run:

```shell
dotnet run --project Generator/Generator.csproj
```

The application displays the next scheduled run and asks:

```text
Run immediately? [Y/N]
```

- Press `Y` to generate and publish immediately.
- Press `N` to wait for the next scheduled time.
- Press `Q`, `Escape`, or `Ctrl+C` to stop.

When input is redirected, the application skips the prompt and waits for the next scheduled run.

## Publishing warning

A successful run is a publishing operation. It can overwrite the configured root feed files, stage those files, create a commit named `Content generated on <UTC timestamp>`, and run `git push`. Even when feed contents are unchanged, the application still attempts to push the current branch.

Run the application only from the intended repository, branch, and remote. Keep unrelated changes out of the generated feed files.

## Generated data

- Final JSON Feed files are written in the repository root.
- Per-run working files are recreated under `.temp/<configuration-name>`.
- Timing history is stored at `.temp/timings.json`.
- `.temp` is ignored by Git.
- Existing feeds are preserved when no articles succeed or a candidate feed fails validation.

## Troubleshooting

### LM Studio connection failures

Confirm that LM Studio is running, the configured model is loaded, and `Generator/appsettings.json` uses the same loopback port. The service must support streamed OpenAI-compatible chat completions.

### Chrome or Agent360 failures

Confirm that Google Chrome is installed in `/Applications`, and that `node`, `npm`, and `npx` are available on `PATH`. The first Agent360 launch may require npm registry access.

### Git push failures

Check `git remote -v`, the token's repository permissions, and the configured Git username and email. The application disables interactive Git credential prompts, so invalid credentials fail immediately.

### Feed files remain unchanged

The application preserves the existing feed when every article fails, the generated items are invalid, or the merged feed would contain fewer items than the existing feed depth. Review the console warnings and `.temp` working files for the affected configuration.
