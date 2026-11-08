#!/bin/sh
set -eu
if [ "$(id -u)" -ne 0 ]; then echo 'Run using sudo.' >&2; exit 1; fi
/bin/launchctl bootout system/com.companyname.clms.agent.service 2>/dev/null || true
if [ "${1:-}" = '--purge-credentials' ]; then /Library/CLMS/Service/CLMS.Agent.Service --reset; fi
# Fixed individual files only. Keep binaries, vault, and checkpoints unless explicitly reset above.
/bin/rm -f /Library/LaunchDaemons/com.companyname.clms.agent.service.plist
/bin/rm -f /Library/LaunchAgents/com.companyname.clms.agent.companion.plist
echo 'Uninstalled launch integration. Sign out to stop an existing companion. Revoke retired units on the server.'
