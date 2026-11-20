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
