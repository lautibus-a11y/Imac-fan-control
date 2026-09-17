# iMacFanControl.UI (Phase 3 Roadmap)

This folder will house the native modern Windows WinUI 3 / Windows App SDK desktop interface.

### Structure:
- `/Dashboard`: Minimalist dark mode overview with live CPU/GPU/HDD temperature cards and fan RPM gauges.
- `/Fans`: Individual fan inspection, manual RPM slider controls with hardware safety limits.
- `/Curves`: Graphical fan curve editor with temperature hysteresis and sensor selection.
- `/Profiles`: Preset selector (Silent, Normal, Gaming, Custom) with proportional RPM scaling.
- `/Settings`: General application preferences (startup, tray, polling intervals).

*Note: Per architecture specifications, development of UI controls is held until Phase 1 hardware diagnostic and Phase 2 controlled write tests are verified on physical iMac hardware.*
