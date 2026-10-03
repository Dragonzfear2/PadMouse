# Releasing a new version

1. Bump the version in `src/AppInfo.cs`. There are three `Assembly...Version` lines.
2. Add the changes to `CHANGELOG.md`.
3. Commit, then tag and push:

   ```
   git tag v1.2.0
   git push origin main --tags
   ```

4. GitHub Actions builds `PadMouse.exe` and publishes it as a release, signing it first if signing secrets are set.
5. Running copies of PadMouse check for updates once a day. They download the new `PadMouse.exe` from the release and update themselves.

The update check needs `AppInfo.GitHubRepo` in `src/AppInfo.cs` to be set to your `owner/repo`. The release asset must be named exactly `PadMouse.exe`.
