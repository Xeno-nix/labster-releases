#!/bin/sh
set -eu
if [ "$(id -u)" -ne 0 ]; then echo 'Run using sudo.' >&2; exit 1; fi
if [ "$#" -ne 2 ]; then echo 'Usage: install-macos.sh SERVICE_PUBLISH_DIR CLMS.Agent.app' >&2; exit 1; fi
service_source=$(cd "$1" && pwd -P)
ui_source=$(cd "$2" && pwd -P)
test -x "$service_source/CLMS.Agent.Service"
test -f "$ui_source/Contents/Info.plist"
case "$service_source" in /Library/CLMS*) echo 'Publish outside the install directory.' >&2; exit 1;; esac
case "$ui_source" in /Applications/CLMS.Agent.app*) echo 'Supply an app outside the install directory.' >&2; exit 1;; esac
# Enterprise installation: validate signed/notarized artifacts before installing root-executed code.
/usr/bin/codesign --verify --deep --strict "$service_source/CLMS.Agent.Service"
/usr/bin/codesign --verify --deep --strict "$ui_source"
/usr/sbin/spctl --assess --type execute "$ui_source"
/bin/launchctl bootout system/com.companyname.clms.agent.service 2>/dev/null || true
/usr/bin/install -d -o root -g wheel -m 755 /Library/CLMS /Library/CLMS/Service
/usr/bin/install -d -o root -g wheel -m 700 '/Library/Application Support/CLMS/Agent'
/usr/bin/ditto "$service_source" /Library/CLMS/Service
/usr/bin/ditto "$ui_source" /Applications/CLMS.Agent.app
/usr/sbin/chown -R root:wheel /Library/CLMS/Service /Applications/CLMS.Agent.app
/bin/chmod -R go-w /Library/CLMS/Service /Applications/CLMS.Agent.app
cat > /Library/LaunchDaemons/com.companyname.clms.agent.service.plist <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>Label</key><string>com.companyname.clms.agent.service</string>
<key>ProgramArguments</key><array><string>/Library/CLMS/Service/CLMS.Agent.Service</string></array>
<key>RunAtLoad</key><true/><key>KeepAlive</key><true/>
<key>Umask</key><integer>63</integer>
<key>UserName</key><string>root</string><key>WorkingDirectory</key><string>/Library/CLMS/Service</string>
<key>ThrottleInterval</key><integer>10</integer>
<key>StandardOutPath</key><string>/var/log/clms-agent.log</string>
<key>StandardErrorPath</key><string>/var/log/clms-agent-error.log</string>
</dict></plist>
PLIST
cat > /Library/LaunchAgents/com.companyname.clms.agent.companion.plist <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>Label</key><string>com.companyname.clms.agent.companion</string>
<key>ProgramArguments</key><array><string>/Library/CLMS/Service/CLMS.Agent.Service</string><string>--watch-ui</string></array>
<key>RunAtLoad</key><true/><key>KeepAlive</key><true/>
<key>Umask</key><integer>63</integer><key>LimitLoadToSessionType</key><string>Aqua</string>
<key>ThrottleInterval</key><integer>10</integer>
</dict></plist>
PLIST
/usr/sbin/chown root:wheel /Library/LaunchDaemons/com.companyname.clms.agent.service.plist /Library/LaunchAgents/com.companyname.clms.agent.companion.plist
/bin/chmod 644 /Library/LaunchDaemons/com.companyname.clms.agent.service.plist /Library/LaunchAgents/com.companyname.clms.agent.companion.plist
/usr/bin/plutil -lint /Library/LaunchDaemons/com.companyname.clms.agent.service.plist /Library/LaunchAgents/com.companyname.clms.agent.companion.plist
/bin/launchctl bootstrap system /Library/LaunchDaemons/com.companyname.clms.agent.service.plist
echo 'Installed. Open /Applications/CLMS.Agent.app to register. The companion starts at the next login.'
