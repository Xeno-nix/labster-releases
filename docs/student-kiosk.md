# CLMS student kiosk pilot

Run this on a test Windows VM or dedicated lab workstation. Use a test student
and an inbox you control. The kiosk requires a registered agent, a trusted HTTPS
server, and Brevo configuration for verification and password reset.

## Run the pilot

1. Apply Database/migrations/2026-11-20-student-kiosk.sql to the test database.
   Choose an existing student enrolled in a non-deleted class whose academic
   term covers today in Philippine time. Add the student's verified school
   email using a trusted roster. The app checks current-term enrollment.
   Existing accounts keep their passwords when updating roster data.
   
   ```sql
   INSERT INTO student_kiosk_accounts (student_id, email)
   VALUES ('TEST_ID', 'test-student@your-school.example');
   ```

2. Run CLMS.Web from Rider using its HTTPS profile. Open CLMS.Agent normally from
   the Windows account the kiosk will use. Register and approve the unit with
   the existing Unit Registry workflow, then quit the normal agent. The agent
   credentials belong to that Windows account.
3. Build the agent and start its Windows executable with the kiosk flag. From
   the CLMS repository root in PowerShell:

   ```powershell
   dotnet build CLMS.Agent
   & .\CLMS.Agent\bin\Debug\net10.0-windows10.0.22621.0\CLMS.Agent.exe --kiosk
   ```

   If your build uses a runtime-specific output directory, use the agent executable
   shown by the build. Keep the normal agent closed while testing kiosk mode.
4. Optionally add a file named kiosk-apps.json beside the executable. Its JSON
   object maps button labels to absolute executable paths. Test with a simple app
   that starts its own process and exits normally. In a managed installation,
   staff must control write access to the install folder and its app list.

   ```json
   {
     "Calculator": "C:\\LabApps\\ExampleCalculator.exe"
   }
   ```

   Replace the example path with an installed approved desktop executable.

## Sample student data

After applying the migration once, open
[Database/samples/student-kiosk.sql](../Database/samples/student-kiosk.sql) in
your MySQL editor connected to the **test** database. Replace the example email
with a real inbox you control, then execute the whole file in one connection.
It creates student `TEST-KIOSK01`, enrolls them in the first available current-term
class, and creates their kiosk account. If no eligible class exists, create a
term covering today and a class in it using the web app, then rerun the script.
The script does not change existing passwords or emails on reruns.

First sign-in: use the stored email and `TEST-KIOSK01` as the password. Read the
emailed six-character, case-sensitive code (uppercase, lowercase, and numbers),
save a password of 15–128 characters that does not contain the
student ID and is not the email, then sign in again with the new password.
Configure `Brevo:ApiKey`, `Brevo:SenderEmail`, and `Brevo:SenderName` on the web
server for email delivery. There is no email verification bypass in this sample.
Sessions stay empty until a successful sign-in with the permanent password;
the server creates the session and linked utilization log automatically.

## Test from another Windows computer

The server PC runs CLMS.Web and accesses MySQL. The other PC only runs CLMS.Agent.
Both computers must be able to reach each other on the lab LAN. These examples
use server name `clms-test` and HTTPS port `7190`; replace the name with your
server's actual resolvable hostname throughout. `localhost` on the other PC
means that other PC, so do not use it as the server address there.

### Prepare HTTPS on the server PC

Use a server certificate valid for that hostname, with its issuer trusted by
both computers. The normal localhost development certificate does not cover
your server's LAN hostname. For an isolated lab test, the following creates a
short-lived self-signed certificate in the server user's certificate store.
Run in PowerShell from the repository root (skip this if IT already supplied
a suitable certificate):

```powershell
New-Item -ItemType Directory -Force .\artifacts\kiosk-tls | Out-Null
$kioskCert = New-SelfSignedCertificate -DnsName 'clms-test' -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddDays(30)
$kioskCertPassword = Read-Host 'Choose a temporary PFX password' -AsSecureString
Export-PfxCertificate -Cert $kioskCert -FilePath .\artifacts\kiosk-tls\server.pfx -Password $kioskCertPassword
Export-Certificate -Cert $kioskCert -FilePath .\artifacts\kiosk-tls\server.cer
```

Copy **only `server.cer`** to the other computer. Verify it is your generated
certificate, then import it on both computers into Current User > Trusted Root
Certification Authorities using the certificate import wizard. On the other PC,
do this while signed in as the Windows user that will run the agent. Keep the
PFX/private key on the server. Remove the test certificate's trust after testing.
If the name does not resolve, configure lab DNS or add a hosts entry on both PCs
mapping `clms-test` to the server's LAN IPv4 address; hosts changes require admin.
Use the hostname URL, not an IP URL, with this certificate.

Start the server in the same PowerShell window:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_Kestrel__Certificates__Default__Path = (Resolve-Path .\artifacts\kiosk-tls\server.pfx).Path
$env:ASPNETCORE_Kestrel__Certificates__Default__Password = [System.Net.NetworkCredential]::new('', $kioskCertPassword).Password
dotnet run --project .\CLMS.Web\CLMS.Web.csproj --no-launch-profile --urls 'https://0.0.0.0:7190'
```

Stop any existing web process using port 7190 first. This overrides the normal
localhost-only binding for this run. Allow inbound TCP 7190 in Windows Firewall
for the intended private lab network only; do not open MySQL to the agent PC.
The server still uses its existing database and Brevo configuration.

On the other PC, run `Test-NetConnection clms-test -Port 7190`, then open
`https://clms-test:7190` in a browser. Continue once the web app loads without a
certificate warning. Agent APIs require HTTPS even in Development.

### Publish and register the other computer

On the development PC with the .NET 10 SDK and MAUI Windows workload, run:

```powershell
dotnet publish .\CLMS.Agent\CLMS.Agent.csproj -f net10.0-windows10.0.22621.0 -c Release -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true --self-contained true -o .\artifacts\kiosk-agent
```

This command targets x64 Windows. Copy the **entire** `artifacts\kiosk-agent`
folder to the other PC, for example `C:\CLMS\Agent`. Do not copy only the EXE,
or copy `%LOCALAPPDATA%\CLMS\Agent` from another PC: device keys are per user
and must be created on the target computer.

1. Run `C:\CLMS\Agent\CLMS.Agent.exe` normally on the other PC.
2. Set its server to `https://clms-test:7190`, choose a free laboratory/unit
   assignment, and request registration.
3. On the web app, open `/admin/unit-registry` as an administrator. Compare the
   displayed physical code and approve that computer. Wait until it connects.
4. Quit the normal agent through its tray controls. Closing its settings window
   may only hide it. Only one agent may run per Windows user.
5. In PowerShell on the other PC, start kiosk mode under that **same user**:

   ```powershell
   & 'C:\CLMS\Agent\CLMS.Agent.exe' --kiosk
   ```

Sign in with the sample account above, complete email verification/password
setup, and sign in again. Run the sample file's final SELECT queries separately
to inspect the active session and utilization log. Click End session and rerun
the SELECT queries: the session should disappear and the log should have a
`time_out`. Add `kiosk-apps.json` beside the copied EXE if you want app buttons.
For this desktop preview, Task Manager can stop `CLMS.Agent.exe` after testing;
the kiosk window intentionally prevents normal close/Alt+F4.

Publishing follows Microsoft's [unpackaged MAUI deployment instructions](https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-unpackaged-cli).
See also [Kestrel HTTPS configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints).

## Enforce the kiosk on a managed VM

Launching with --kiosk on a regular desktop previews the experience. Windows
policy provides the enforcement. On supported Windows Education, Enterprise or
IoT Enterprise, configure Shell Launcher for a dedicated standard-user lab
account. Point its custom shell to the installed agent executable with --kiosk.
Keep Explorer for staff accounts. Configure shell failure recovery and verify
administrator sign-in before switching the lab account. For this pilot, use a
device restart on unexpected shell exit so apps from an interrupted session
do not carry over to the next one.

Pair Shell Launcher with IT-managed application control and account restrictions.
Keep software that can launch arbitrary programs out of the pilot app list.
The kiosk tracks apps it launches and attempts to close their process trees on
session end. Test apps that reuse existing processes or hand work to background
processes separately. User profile files and browsing data need separate cleanup
policy before using shared accounts with real students.

- [Configure Shell Launcher](https://learn.microsoft.com/en-us/windows/configuration/shell-launcher/configure)
- [Shell Launcher and application control](https://learn.microsoft.com/en-us/windows/configuration/shell-launcher/)

## Test cases

1. Start with a registered unit and confirm the full-screen sign-in appears. Try
   Alt+F4. In the managed VM also try switching apps and launching tools before
   login. Verify that staff recovery through the separate admin account works.
2. Enter school email and student ID. Verify that the email code is required
   and apps stay unavailable until a password at least 15 characters long
   is saved and used for sign-in. Try a wrong code, a reused code, and an
   unenrolled student.
3. Launch an approved app after sign-in, then click End session. Verify that
   the app closes and a single unit utilization row has a time-out. Repeat
   after 15 minutes without keyboard or mouse input.
4. Restart the agent before saving the first password. The student ID should
   still work. Restart after saving. The permanent password should work and
   the ID should fail. Use Reset password to verify email recovery.
5. Shut down the VM during a session and boot again. The kiosk should start
   locked. Wait up to two minutes for the old lease to expire, plus one minute
   for the usage log cleanup. Confirm the next sign-in creates one new row.
6. Disconnect the server. Verify that sign-in fails and an open session locks
   when renewal fails. Restore connectivity and sign in again.
