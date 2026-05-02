# SAMBA‑Util  
A modern, cross‑platform graphical tool for managing Samba shares, users, and configuration files.

SAMBA‑Util provides a clean and intuitive interface built with Avalonia UI, allowing administrators to configure Samba without manually editing `smb.conf` or running complex terminal commands.  
It is designed for Linux environments and focuses on simplicity, safety, and transparency.

---

## ✨ Features

### 🔧 Share Management
- Create, edit, and delete Samba shares  
- Configure paths, permissions, masks, and visibility  
- Automatic validation of share names and paths  
- Real‑time reload of Samba configuration

### 👤 User Management
- Add and remove Samba users  
- Synchronize system users with Samba  
- Secure password handling  
- Detection of invalid or expired credentials

### 🛠 System Integration
- Reads and writes `/etc/samba/smb.conf` safely  
- Performs privileged operations using `sudo`  
- Detects incorrect admin passwords without breaking system authentication  
- Displays Samba service status and logs

### 🎨 Modern UI
- Cross‑platform Avalonia interface  
- GitHub‑style theme  
- Responsive layout  
- SVG/PNG icon support  
- Clean, minimal design

---

## 📦 Requirements

- Linux distribution with Samba installed  
- .NET 9.0 runtime (or self‑contained build)  
- `sudo` privileges for administrative actions  
- Access to `/etc/samba/smb.conf`

---

## 🚀 Running SAMBA‑Util

### From source:
```bash
git clone https://github.com/youruser/SAMBA-Util.git
cd SAMBA-Util
dotnet run
