# Contributing

Thanks for helping. A few things to know first.

- **Open an issue before big changes**, so nobody spends a weekend on something that won't land.
- **Official instances are never touched.** Nothing may be added to them, loaded into them or
  changed while they run. A change that needs that won't be merged.
- **Run `dotnet test`** before opening a pull request. CI runs it too, and builds the launcher
  so reviewers can try it.
- **The site** lives in `worker/`. Run it with `npm run dev`. It deploys when `main` changes.
- **Getting a server verified** happens on the [website](https://multivrc.ehko.dev/#apply), not
  through pull requests.

See [Building](../README.md#building) for setup.
