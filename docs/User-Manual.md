# Tambola — User Manual (Help)

The same content is available inside the app under **Help**.

## 1. Getting started (everyone)
1. Open the Tambola URL in **Edge or Chrome**.
   * **Company (Windows) hosting:** you are signed in automatically with your Windows account — there is no password.
   * **Linux hosting:** you sign in with your **domain** (choose from the list), **User ID** and **password**. Choose **Register** the first time. Use *Password* in the top bar to change it, and *Sign out* when you finish on a shared PC. Forgot your password? Ask the Admin for a temporary one; you must pick a new password at your next sign-in. After 5 wrong attempts sign-in is paused for 10 minutes.
   * If your account belongs to a domain that is not allowed, you will see an **Access not permitted** page. Contact the Admin.
2. **First visit only — Registration:** enter your *Display name*, *Employee Code* and *email* (on Linux hosting also your *User ID* and *password*).
   * One Employee Code can be linked to **one** email, and one email to one code. If you see “already registered”, contact the Admin.
3. You land on **Home**: join with a room code, pick a game under **Live games**, look back at **Past games**, see your stats.

The app opens in **dark theme** with **sound on** by default. Use 🌓 (theme) and 🔊 (sound) in the top right; your choice is remembered in your browser.

## 2. Playing (Player)
| Step | What to do |
|---|---|
| Join | Type the 6-character room code on Home → **Join Room** (or **Join** next to an open room). |
| Tickets | In the lobby press **Get a ticket**. The host sets the maximum (default 3). Tickets are final once the game starts. Late joiners may still take tickets in the first few calls. |
| Watch | The big circle shows the **Current Number**; recent calls and the 1–90 board are on the right. Numbers are read aloud and effects play by default; use 🔊 in the header to mute. |
| Cut numbers | **Manual rooms:** tap a *called* number on your ticket (it turns blue). Tap again to un-cut. **Auto rooms:** numbers are cut for you. |
| Claim | When you complete a winning case, press that prize's **Claim** button immediately. |

### Winning cases
* **Four Corners** – first and last number of the top and bottom rows.
* **1st / 2nd / 3rd Row** – all five numbers of that row.
* **Full House** – all 15 numbers.
* **Early Five** (if enabled) – any five numbers.

Each prize has a limited number of winners (e.g. 1st Row: 5). The **first valid claims win**; when the quota is full the prize closes.

### Claim results
* 🎉 **Congratulations** popup (with reward text, if any) — you won, e.g. “winner 3 of 5”.
* ⚡ **So close!** — your claim was correct but others were quicker. Claim faster next time.
* 🤔 **Not a valid claim yet** — numbers are still missing. The message tells you how many wrong claims you have left for that prize (the host sets the limit, default 3). When they run out, that prize is locked for you.
* A button shows 🔒 when you already won it, your wrong-claim limit for it is used up, the prize is full, or the game isn't running. A wrong claim on one prize does not affect your other prizes.

The server checks every claim against the numbers that were really called — marks on your screen don't matter.

### Connection problems
A red “Reconnecting…” bar means the connection dropped. The game restores automatically; your tickets and cuts are kept.

## 3. Hosting (Host)
Hosts are appointed by an Admin; you will see **Create Room** in the menu.

1. **Create Room**
   * *Room name, max players (up to **1500**), max tickets per player.*
   * *Wrong claims allowed per prize:* type a number (default **3**). A player's wrong claim only locks that prize for them once they have used this many; type **0** for unlimited tries.
   * *Number calling:* **Manual** (you press *Call next*) or **Auto** (server calls every 3–15 s).
   * *Number cutting:* **Manual** (players tap) or **Auto** (cut automatically). Fixed for the room.
   * *Prizes:* tick the cases you want, set the **number of winners** for each (e.g. Full House 1, Four Corners 4, each Row 5) and an optional reward text.
2. **Share the room code** shown on the console. Players appear live in the *Players* list; you can **Lock room** or **Remove** a player.
3. **Start game** (needs at least one ticket taken).
4. **Call numbers:** *Call next* in Manual mode. Switch Manual/Auto or change the interval at any time. **Pause / Resume** as needed. Numbers are announced aloud on your device (🔊 in the header mutes).
5. Watch the **Prizes** panel and **Claims & feed**. The game **completes automatically** when every prize is full; you may also press **End game** (or **Cancel room** before starting).
6. Results are shown after the game and kept in *My Games* and under **Past games** on Home.
7. **Delete a finished game** (Past games → *Delete*, or the button on the Results page) when you no longer want it listed. It disappears for you and all players; an Admin can still see it in *Reports → Deleted games* and restore it. Games that are still live cannot be deleted — end or cancel them first.

Hosts cannot hold tickets in their own room.

## 4. Administration (Admin)
Admins are listed in `appsettings.json → Tambola:AdminUsers`.
* **Hosts & Employees:** search; **Make Host / Revoke Host** (any number of hosts); **Activate / Deactivate** an employee (deactivated users cannot play).
* **Reset password** (Linux hosting only): gives you a one-time temporary password to pass to the employee, who must change it at next sign-in.
* **Reports:** totals, leaderboard, **Export CSV**, audit log (registrations, role changes, room creation, deletions), and **Deleted games** with a **Restore** button.

## 5. FAQ
| Question | Answer |
|---|---|
| A browser login box appears | Use Edge/Chrome on a domain-joined PC. Firefox: add the site to `network.negotiate-auth.trusted-uris`. |
| I can't join a room | It may be locked, full, finished, or you were removed. |
| Why can't I cut a number? | It hasn't been called yet. |
| I pressed Claim but got “So close” | Another player completed it first and the prize quota was already full. |
| I mis-registered my code | Ask the Admin to deactivate the record / correct it. |
| I can't find a game that was finished | Look under **Past games**. If the host deleted it, ask the Admin to restore it. |
| I can't sign in (Linux hosting) | Check the domain, User ID and password; after 5 failures wait 10 minutes or ask the Admin to reset your password. |
