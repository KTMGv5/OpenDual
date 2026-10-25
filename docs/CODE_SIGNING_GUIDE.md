# OpenDual Code Signing & SmartScreen Guide

This guide explains how code signing works on Windows, why Windows Defender SmartScreen displays warnings on new binaries, and how to sign **OpenDualSetup.exe**, **OpenDualHub.exe**, and **OpenDualDriver.exe** so they are recognized as trusted.

---

## 1. Why Windows Rejects Unsigned Binaries

When downloading or running an executable on Windows 10/11, Windows Defender SmartScreen evaluates two factors:
1. **Digital Signature**: Whether the executable is signed with an Authenticode certificate from a trusted Certificate Authority (CA).
2. **Reputation**: How many thousands of machines across the globe have downloaded and executed this exact binary hash without malware reports.

If an executable is **unsigned** or **newly compiled**, SmartScreen displays the blue alert:
> *"Windows protected your PC — Microsoft Defender SmartScreen prevented an unrecognized app from starting."*

---

## 2. Local Signing (Instant & 100% Free for Personal Use)

If you are compiling and running OpenDual on your own computer (or sharing within a local network/friends where you can install the root cert):

OpenDual includes an automated local code signing certificate generator and binary signer:

1. Open PowerShell or Command Prompt in the repository root.
2. Run the signing script:
   ```bat
   .\tools\sign_binaries.bat
   ```
   *or in PowerShell:*
   ```powershell
   .\tools\sign_binaries.ps1
   ```

### What This Does:
- Generates a 10-year SHA-256 Code Signing Certificate (`CN=OpenDual Suite, O=OpenDual Development`) in `Cert:\CurrentUser\My`.
- Registers the certificate into `Cert:\CurrentUser\Root` (Trusted Root Certification Authorities).
- Automatically stamps `OpenDualSetup.exe`, `OpenDualHub.exe`, and `OpenDualDriver.exe` with a cryptographic signature and an official RFC 3161 Digicert timestamp.
- **Result**: On your PC, Windows treats OpenDual as a verified publisher. Double-clicking the installer shows verified publisher details with no SmartScreen warnings.

---

## 3. Public Distribution (Signing for Other Users)

If you intend to distribute OpenDual to thousands of users on GitHub without them seeing SmartScreen warnings, you need a certificate issued by a Microsoft-recognized Certificate Authority:

### Option A: Microsoft Azure Trusted Signing (Recommended for Modern Devs)
- **Cost**: ~$9.99/month (subscription based, cancel anytime).
- **Pros**: 
  - Directly integrated into Windows and Microsoft Defender.
  - 100% cloud-based (no physical USB cryptographic tokens required).
  - Can be integrated into GitHub Actions CI/CD workflows using Azure credentials.
- **Provider**: Azure Portal -> Trusted Signing Accounts.

### Option B: Certum Open Source Developer Certificate
- **Cost**: ~€28 to €45 / year.
- **Pros**: The most economical choice for open-source developers worldwide.
- **Provider**: [Certum.eu Open Source Code Signing](https://www.certum.eu/en/open-source-code-signing/).

### Option C: DigiCert / Sectigo EV Code Signing
- **Cost**: ~$400 - $600 / year.
- **Pros**: Instant SmartScreen reputation on day 1 (bypasses the reputation warm-up period).
- **Cons**: Expensive and requires a physical FIPS USB hardware token or enterprise cloud HSM.

---

## 4. Signing via Command Line (Manual Reference)

If you have your own `.pfx` file or certificate installed:

```cmd
signtool sign /f "my_certificate.pfx" /p "YourPassword" /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "dist\OpenDualSetup.exe"
```

Or using an installed certificate by thumbprint:

```cmd
signtool sign /sha1 YOUR_CERT_THUMBPRINT /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "dist\OpenDualSetup.exe"
```

To verify a binary's digital signature:

```cmd
signtool verify /pa /v "dist\OpenDualSetup.exe"
```
