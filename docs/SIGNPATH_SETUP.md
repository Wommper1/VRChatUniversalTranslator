# SignPath setup: what to do, in order

This repository is **prepared** for free code signing through [SignPath Foundation](https://signpath.org). Nothing is signed
yet and nothing here needs SignPath to keep working: until the steps below are done, the Release workflow builds and
checks an **unsigned** package and prepares a draft release. Rules and settings below come from SignPath's
own documentation as of October 2026; read their current pages before applying, because they may change.

## What is already in the repository

| File | What it does |
| --- | --- |
| `.github/workflows/release.yml` | On a `v*` tag: builds the package on GitHub, runs the checks, creates a **draft** pre-release. On a pull request that touches the release files, or when run by hand: build and check only. It does **not** contain the signing step yet (see step 3 for why). |
| `.signpath/release-signing-job.yml` | The signing job, ready to paste into `release.yml` once SignPath has accepted the project and the repository allows SignPath's action. It is not a workflow by itself. |
| `scripts/build-release.ps1` | The same package build as before. It now leaves the commit id out of the file version, so the version in each file's properties is exactly `x.y.z` for every build of a release (SignPath requires that). |
| `scripts/check-package.ps1` | Fails if the package is missing a required file, holds something that looks like a setting, model, log or key, or contains the build machine's folder name, the maintainer's user name or an e-mail address. Writes `SHA256SUMS.txt`. |
| `scripts/verify-signed-package.ps1` | After signing: each of our five files must have a valid, timestamped signature; every other file must be byte-identical to the unsigned build. |
| `scripts/write-release-body.ps1` | Writes the draft release text from `RELEASE_NOTES.md` and the real checksum. |
| `.signpath/artifact-configuration.xml` | The signing rules to paste into SignPath: our five files only, with product name and version restrictions. |
| `docs/CODE_SIGNING.md` | The draft code signing policy text SignPath requires you to publish. |

## 1. Before applying

- [ ] Turn on two-factor authentication for your GitHub account (SignPath requires it for everyone with a role).
- [ ] Read the [SignPath Foundation conditions](https://signpath.org/terms). The ones that matter here: an OSI-approved licence for
      every component (MIT, with MIT, BSD and Apache libraries), a public repository, a project that is already released and
      described on its download page, no malware, only binaries built from your own source, a manual approval for every
      release, and a code signing policy shown on the home page and the release pages.
- [ ] Check that you are happy with the roles in `docs/CODE_SIGNING.md` (today: you, in all three roles) and with the privacy
      paragraph. Edit the text if anything is not true.
- [ ] Merge the pull request that added these files, so the workflow, scripts and policy are on `main`.

## 2. Apply (you do this; it cannot be done for you)

- [ ] Go to <https://signpath.org/apply.html>, fill in the form for the project (name `VRChat Universal Translator`, the
      GitHub address, the licence) and submit it. SignPath decides at its own discretion; third-party reports say it usually
      takes a few business days.
- [ ] If the form asks for a link to your code signing policy, publish the policy first (step 5) without waiting.

## 3. After SignPath accepts the project

In SignPath:

- [ ] Note your **organization ID** and create (or open) the project; its **slug** is its short name, for example
      `vrchat-universal-translator`.
- [ ] Add the predefined trusted build system **GitHub.com** to the organization and link it to the project
      (SignPath's page: "Trusted build systems, GitHub").
- [ ] Set the project's default **artifact configuration** to the contents of `.signpath/artifact-configuration.xml`.
- [ ] Check the **signing policies**. Keep a policy for real releases (`release-signing`) that requires a **manual approval**,
      and, if SignPath offers it, a test policy for a first dry run. Add yourself as an approver. Note each policy's slug.
- [ ] In the build policies, require GitHub-hosted runners and disallow re-runs. If SignPath offers a repository policy check,
      the branch ruleset already on `main` (deletion and force-push blocked, build check required) is what it looks for.
- [ ] Create an **API token** for a SignPath user who may submit requests for that project and policy.

**Allow SignPath's action in GitHub, then add the signing job.** This repository only allows GitHub's own actions (Settings, Actions, General), and
SignPath's action is not one of them. A workflow that mentions a disallowed action does not start at all (the run shows
"startup failure"), which is why the signing job is not in `release.yml` yet.

- [ ] In GitHub: Settings, Actions, General, Actions permissions. Keep "Allow ... select actions" and add
      `signpath/github-action-submit-signing-request@*` to the allowed list. This keeps the setting at "selected" (the weekly security scan only complains
      about "all"), and it lets exactly one outside action run. Only do this after SignPath has accepted the project.
- [ ] Paste the job from `.signpath/release-signing-job.yml` into `.github/workflows/release.yml` and make the three small edits its comments list
      (through a pull request, like any change).

In GitHub (repository Settings, Secrets and variables, Actions):

- [ ] Secret `SIGNPATH_API_TOKEN`: the token.
- [ ] Variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG` (start with the test policy
      if you have one).
- [ ] Variable `SIGNPATH_ENABLED` set to `true`. This is the switch: set it back to `false` (or delete it) and releases are
      built unsigned again.

## 4. The first signed release

1. Prepare the release on a branch as usual (version in `VRChatTranslator.App.csproj`, a new section at the top of
   `RELEASE_NOTES.md`, `VERIFICATION.md`), merge it by pull request, then tag the merged commit and push the tag:
   `git tag vX.Y.Z` then `git push origin vX.Y.Z`.
2. The Release workflow runs. When it reaches the signing step, SignPath waits for your approval (you get a message from
   SignPath; approve the request there).
3. The workflow checks the signatures, assembles the signed ZIP and creates a **draft pre-release**.
4. Open the draft on the Releases page, download the ZIP and run the usual checks (the startup tests from the extracted
   program, `VERIFICATION.md`), read the text, then publish.
5. If you used a test policy, delete the draft and the tag (`gh release delete vX.Y.Z --yes` then
   `git push origin --delete vX.Y.Z`), switch the variable `SIGNPATH_SIGNING_POLICY_SLUG` to the real policy and tag again.

## 5. When the first signed release is out

- [ ] Show the code signing policy (the text from `docs/CODE_SIGNING.md`, including the credit line) in a "Code signing
      policy" section of the README and on the release pages. `scripts/write-release-body.ps1` already adds the signed wording.
- [ ] Update the three places that say the program is unsigned: `README.md` (install step 4), `SECURITY.md` (known
      limitations) and `RELEASE_STATUS.md` ("Code signing").
- [ ] Expect SmartScreen to keep warning for a while. Microsoft builds reputation automatically from downloads; a signature
      helps it recognise the publisher but does not remove the warning at once. The publisher Windows shows is
      "SignPath Foundation", not the company name.

## What can go wrong

- A Release run that ends as "startup failure" without running any step: the workflow file mentions an action this repository does not allow.
  Remove that action or allow it in Settings, Actions, General.
- The workflow stops with "The tag does not match the app version": the tag must be `v` plus the version in the project file.
- The workflow stops with "The first heading in RELEASE_NOTES.md...": the newest notes section must start with the version.
- SignPath refuses a file: its product name or version differs from `.signpath/artifact-configuration.xml`. The three library
  projects say `1.0.0`; if they ever get their own version, change the file.
- The signed package check fails with "Changed although it is not one of our own files": SignPath altered a third-party file;
  do not publish, and tell SignPath support.
- Anything unexpected in the signing step: set `SIGNPATH_ENABLED` to `false` and build unsigned while you sort it out.

## What this does not change

The release is still prepared on a branch and merged by pull request before it is tagged, and a person still reads and
publishes the draft. The workflow never publishes anything by itself.
