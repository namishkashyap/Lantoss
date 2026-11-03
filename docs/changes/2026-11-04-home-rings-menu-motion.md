# 2026-11-04: Home rings, glow and menu animation

Owner's request: make the glow around "This PC" on Home a proper circle, thin the wavy ring lines, add 2 or 3 more coloured rings with more glow, and make the menu (hamburger) open faster and correctly every time. No change to names: the web page and Home already show the name set in hopo (Preferences.DeviceName), not a separate Windows name.

## Items

- [ ] 1. **Round glow.** The aura behind "This PC" used a 60 px DropShadowEffect, which the GPU blur draws as a rounded square. Replace it with a circle filled by a radial gradient.
- [ ] 2. **Rings.** Thinner wavy lines (0.7 to 0.5 px), three more rings in new colours (amber, sky blue, coral), and a stronger glow (wider, brighter halo).
- [ ] 3. **Menu opens every time.** The second open had nothing to animate: the old animations still held the end values, so the new ones ran from 1 to 1. Clear the old animations first (menu panel, scrim and the row rise).
- [ ] 4. **Menu opens faster.** Panel 0.58 s to 0.36 s, scrim 0.4 s to 0.25 s, rows start sooner and rise in 0.32 s.

## Answered, no change

- The Phone panel shows only the PC's local Wi-Fi address (http://192.168.x.x:port). It holds no password or token, cannot be reached from the internet, and a phone still needs Allow on the PC (or the access code). It is safe to show in a video; keep the access code off screen.
