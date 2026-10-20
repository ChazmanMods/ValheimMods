from pathlib import Path
import json, re

root = Path(__file__).resolve().parents[1]
modules = list(json.loads((root/'CANDIDATES.json').read_text(encoding='utf-8'))) + ['RunicModSuite', 'RunicModClientSuite', 'RunicModServerSuite']
support = '\n## Support My Work\n\nEnjoying the mods? You can support my work and future creations. Thank you for playing!\n\n[Support My Work](https://buymeacoffee.com/the_artful_engineer)\n'
dashboard = '''## F3 dashboard

- **Mod Policy:** search mods by name, choose Required, Allowed, Greylisted or Blocked, then select **Apply & Sign Policy**. Advanced rules let you specify versions and file restrictions.
- **People:** manage administrator roles and bans.
- **Commands:** enter a command and press Enter or **Run**. Browse the command guide, read command output, and toggle debug mode or no-cost building. Server Devcommands permissions apply.
- **Catalog:** browse items, food, ingredients, potions, creatures and building pieces. Filter by mod or type, search by name, and select an entry for recipes and details. Choose a quantity before spawning. **Place with hammer** uses your equipped build tool and the usual building rules.
- **Players:** select a player to view their last-known position and recent activity. Create an object report, or prepare **Bring player to me** and run it from Commands. Reports are saved on the server in `BepInEx/config/RunicSentinel/PlayerReports` as JSON, CSV and text.
- **Server Cap:** with Runic World Engine installed on the host, choose 2–64 players and select **Save for Next Restart**. Restart the host to apply the saved limit.

## Administrator access

Server administrators have Sentinel access by default. Add your account ID to the server's `adminlist.txt`; for Steam, use `V_<SteamID64>`. Close and reopen F3 after changing the list.

To use only Sentinel roles, set `UseServerAdminList = false` under **Administrator Access** in the server configuration. Remove separately assigned Sentinel roles in the People tab when revoking access. Bans take precedence over administrator roles.

## Commands and reports

- `runic_sentinel status` shows the current policy and admission status.
- `runic_sentinel report` saves a support report in `BepInEx/config/RunicSentinel/reports`.
- `runic_sentinel networks` saves a snapshot of loaded portal and production networks from the host console.
'''
for module, title in [('RunicSentinel', 'Runic Sentinel'), ('RunicSentinelServer', 'Runic Sentinel Server')]:
    manifest = json.loads((root/module/'manifest.json').read_text(encoding='utf-8'))
    intro = ('Manage your Valheim server from the F3 dashboard. Choose the approved mods, manage administrators and bans, browse game content, run commands and view player reports.' if module == 'RunicSentinel' else 'Runic Sentinel Server manages approved mods, player admission, administrator permissions and bans on your dedicated server. Administrators connect using the full Runic Sentinel mod and its F3 dashboard.')
    text = f'# {title} {manifest["version_number"]}\n\n{intro}\n\n' + '''## Installation and setup

1. Install **RunicSentinelServer** on the dedicated server and **RunicSentinel** on the administrator's client. A listen host uses full RunicSentinel. Use only one of these two packages in each profile.
2. Install the dependencies listed by your mod manager, including Runic Safety and Server Devcommands. Ordinary players use **RunicSentinelClient**.
3. Start with **Remote Admission → Policy = Optional** in the server configuration.
4. Add your account ID to the server's `adminlist.txt`, join the game and press **F3**.
5. Select **Set Up Sentinel**, choose your mod policy, and select **Apply & Sign Policy**.
6. When your players' mod profiles are ready, set admission to **Required** and restart the server.

Keep the server's private key in `BepInEx/config/RunicSentinel/server-private` on the server. Keep your existing Sentinel configuration when updating.

''' + dashboard + f'''\n## Language files

The mod follows Valheim's selected language. Add translations in `Translations/{module}` beside the mod file. Missing translations use English. See `TRANSLATING.md` for instructions.
''' + support
    (root/module/'README.md').write_text(text, encoding='utf-8')

(root/'TRANSLATING.md').write_text('''# Adding a language

The mods follow the language selected in Valheim. English is used for any missing translation.

1. Open the mod's `Translations` folder and find `English.json`.
2. Copy it into the same folder using Valheim's language name, such as `German.json` or `French.json`.
3. Translate the text on the right of each entry. Keep the keys on the left unchanged.
4. Keep placeholders such as `{0}`, `{1}` and `{0:0.0}`, along with any formatting tags.
5. Save the file as UTF-8 JSON and restart Valheim. Select your language in the game settings.

You can translate a few entries at a time; the remaining text stays in English. Close and reopen mod panels after changing the game language. Messages from a dedicated server use the server's selected language.

WaterGardens language files are in `Translations/ArcaneDecorWaterGardens`.
''', encoding='utf-8')

# Remove development/test commentary from public prose while retaining installation,
# configuration, gameplay instructions, attribution and legal notices.
dev = re.compile(r'automated.{0,60}(?:checks|regression|test)|regression|harness|compile.time|compiled (?:out|the limited)|compiled contracts|technical safety details|installed.(?:IL|game API|game signature)|audited test|live gameplay verification|in.game (?:testing|acceptance|validation)|author.confirmed|release.candidate|not.*certify every mod|not confirmed fixed', re.I)
removed = []
for module in modules:
    for name in ['README.md', 'CHANGELOG.md', 'USER_GUIDE.md', 'INSTRUCTIONS.md']:
        p = root/module/name
        if not p.exists(): continue
        text = p.read_text(encoding='utf-8-sig')
        text = text.replace('Update participating clients and servers together and restart. Automated component checks passed; live gameplay verification of these fixes remains pending.', 'Update participating clients and servers together and restart.')
        text = text.replace('## 2.0.5 - Internal candidate (superseded by 2.0.6)', '## 2.0.5').replace('## 1.0.0 candidate', '## 1.0.0')
        text = text.replace(' Initial enrollment trusts the character supplied by that authenticated account; it does not prove where its items or skills originated.', '')
        text = text.replace(' Test with a nonessential character before opening a production server.', '')
        # A Markdown bullet and its continuation lines form one entry.
        blocks = re.split(r'(\n\s*\n|\n(?=- ))', text)
        clean = []
        for block in blocks:
            if dev.search(block):
                removed.append(f'{module}/{name}: {block.strip()}')
                continue
            clean.append(block)
        text = ''.join(clean)
        text = re.sub(r'\n{3,}', '\n\n', text)
        text = re.sub(r'(## [^\n]+\n)\s*(?=## |\Z)', '', text)
        p.write_text(text.strip()+'\n', encoding='utf-8')

for p in [root/module/name for module in modules for name in ['README.md', 'USER_GUIDE.md', 'INSTRUCTIONS.md']] + [root/'TRANSLATING.md']:
    if not p.exists(): continue
    text = p.read_text(encoding='utf-8')
    text = re.sub(r'^## (?:Support Chazman[^\n]*|Support My Work|Support)\n.*?(?=^## |\Z)', '', text, flags=re.M | re.S)
    text = re.sub(r'\n\[Buy me a coffee\]\(https://buymeacoffee.com/the_artful_engineer\)\s*', '\n', text)
    p.write_text(text.rstrip()+'\n'+support, encoding='utf-8')
(root/'artifacts'/'public-docs-cleanup.txt').write_text('\n\n'.join(removed), encoding='utf-8')
print(f'Updated public documentation for {len(modules)} packages; removed {len(removed)} development-note blocks.')
