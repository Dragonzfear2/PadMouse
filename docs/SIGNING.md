# Code signing

## What it is

Signing attaches a verified publisher name to `PadMouse.exe`, along with a tamper-proof seal. Windows then shows "Publisher: <your name>" instead of "Unknown publisher". If anyone modifies the file, the signature breaks.

## Do you need it?

Only if other people will download PadMouse. Without a signature, downloaders see the blue SmartScreen warning ("Windows protected your PC") and have to click **More info → Run anyway**. On your own PC, signing makes no difference.

Signing doesn't remove that warning instantly. Since 2024, even EV certificates don't skip SmartScreen. The warning goes away as your signed downloads build reputation.

## Options (prices as of late 2026)

| Option | Cost | Notes |
|---|---|---|
| Azure Artifact Signing (Trusted Signing) | $9.99/month | Individual developers are only accepted in the USA and Canada for now. In the UK, only registered organisations can use it. Works well with GitHub Actions. |
| OV certificate (Sectigo, DigiCert, SSL.com…) | ~$150–300/year | Available to UK individuals after an ID check. The private key lives on a hardware token or in a cloud HSM. |
| EV certificate | $400+/year | No longer skips SmartScreen, so it's not worth it for PadMouse. |
| Microsoft Store (MSIX) | Free | Microsoft signs the package for you and there are no SmartScreen warnings. Needs a Store developer account and an MSIX package. |
| Self-signed | Free | Only trusted on PCs where you install the certificate yourself. |

## Turning signing on

The build is ready for signing. Nothing is signed until you provide a certificate.

- **Local builds:** set the following, then run `build.bat`:
  - `SIGN_PFX=C:\path\cert.pfx`
  - `SIGN_PASSWORD=...`
- **GitHub Actions:** add these repository secrets:
  - `SIGN_CERT_BASE64`: your `.pfx` file, base64-encoded. In PowerShell: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))`
  - `SIGN_PASSWORD`

  Every release is then signed automatically.
- **Azure Artifact Signing / cloud HSM:** replace the "Sign" step in `.github/workflows/build.yml` with the provider's action, for example `azure/trusted-signing-action`.
