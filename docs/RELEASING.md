# Releasing GZJam

How changes reach DJs: branches, version numbers, betas and the release itself. Before 1.0,
releases came often and straight from main. From 1.0 on they come less often, so a DJ can count
on each one, and most of this page is about that.

## Branches

- **main** is where everything goes: a short-lived branch for each issue, a pull request, the
  checks on all three systems, then a squash merge. There's no `develop` branch.
- **release/1.0** (and later release/1.1, …) exists only when it's needed: a fix that DJs on the
  current release need now, while main already holds unreleased features that aren't ready to
  ship. Made from the release's tag, it gets only fixes, by pull request, as main does. Most of
  the time there's no such branch, and fixes are released from main.
- Only the newest release gets fixes. Once 1.1 is out, a fix goes into 1.1.1, not 1.0.x.

## Version numbers

- **1.0.1, 1.0.2…** fixes only.
- **1.1, 1.2…** new features (a feature in a release makes it a minor one, however small).
- **2.0** for a change in what GZJam is, or one that leaves something behind (a library older
  versions can't open, a dropped system).
- Every build that leaves this computer gets a new number. A tag never moves and is never used
  twice, not even for a draft.

## Each pull request

- Adds what a DJ would notice to `## Unreleased` in CHANGELOG.md, under New, Changed or Fixed.
- Adds it to ROADMAP.md's *Finished, coming in the next release*, and takes it off *Planned* or
  *Being considered*.
- Updates docs/GUIDE.md and docs/FEATURES.md where the change shows.
- Is built and tested here first: `dotnet build GZJam.slnx -c Release -p:TreatWarningsAsErrors=true`
  (as CI does: a warning fails it), then `dotnet test`.
- Closes its issue: `Fixes #N`.

## Betas

A feature that changes sound, packaging, the library's format, or how a night runs is worth a beta
before everyone gets it.

- Tag `v1.1.0-beta.1` (then beta.2, …) from main. The release workflow marks it a prerelease, and
  GZJam's update notice skips prereleases, so only the DJs given the link try it.
- To hand one DJ a build without any release: Actions › Release › Run workflow, with a version
  such as `1.1.0-test.1`. The packages are attached to the run.

## Releasing from main

1. Decide the number (fixes only: patch; any feature: minor), and say why.
2. For a minor release (1.1, 1.2…): go through [TESTING.md](TESTING.md) on at least one computer,
   and on each system the release touches (sound, packaging, a system's own code). A fix release
   needs this only for a fix in those areas.
3. CHANGELOG.md: rename `## Unreleased` to `## 1.1.0 (date)`, and start a new
   `## Unreleased` with "Nothing yet since 1.1.0."
4. ROADMAP.md: "Up to date as of 1.1.0", the finished items into *What GZJam does today*, and
   *Finished, coming in the next release* back to "Nothing yet since 1.1.0."
5. GZJam.App.csproj: `VersionPrefix` to the next minor (1.2.0), so local builds read 1.2.0-dev.
   (`A_build_made_here_is_ahead_of_the_newest_release` fails until it's raised.)
6. Commit those to main, then tag and push: `git tag -a v1.1.0 -m "GZJam 1.1.0"`,
   `git push origin v1.1.0`.
7. When the release workflow is done, check the draft has all five packages, then publish it as
   the latest release. Installed GZJams say so at their next start.
8. A post for the Facebook group (`branding/social/`).

## Releasing a fix from a release branch

When main holds unreleased features, and DJs need a fix now:

1. Make the branch from the tag, once: `git switch -c release/1.0 v1.0.0`, and push it. Raise its
   `VersionPrefix` to 1.0.1 (local builds there read 1.0.1-dev).
2. Fix it on main first, by pull request, as usual. Then a pull request into release/1.0 with the
   same change (`git cherry-pick`), and its CHANGELOG entry under `## 1.0.1 (date)` there.
3. Tag `v1.0.1` on release/1.0, push it, and publish as above. Then raise release/1.0's
   `VersionPrefix` to 1.0.2.
4. On main: add the 1.0.1 section to CHANGELOG.md too, below `## Unreleased` and above 1.0.0, and
   take that fix out of Unreleased, so main's changelog tells the whole story. ROADMAP.md: "Up to
   date as of 1.0.1".

The rules for release/* branches match main's (changes by pull request, the checks passing), and
CI runs on pull requests into them.
