# SAMBA‑Util  
A modern, graphical tool for managing Samba shares, users, and configuration files.

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
- Avalonia interface  
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

INSTALLATION

AUR (Arch Linux / Manjaro / EndeavourOS)
SAMBA‑Util is available in the Arch User Repository.

Install with yay:
```bash
yay -S samba-util
```
Install with paru:
```bash
paru -S samba-util
```
Manual AUR clone:

```bash
git clone https://aur.archlinux.org/samba-util.git
cd samba-util
makepkg -si
```
AppImage (All Linux distributions)
A portable AppImage build is available in the Releases section.
https://github.com/mijocecr/samba-util/releases

Make it executable:
```bash
chmod +x SAMBA-Util-x86_64.AppImage
```
Run it:
```bash
./SAMBA-Util-x86_64.AppImage
```
---

## 🚀 Running SAMBA‑Util

### From source:
```bash
git clone https://github.com/mijocecr/samba-util.git
cd samba-util
dotnet run
```


PROJECT STATUS

SAMBA‑Util is stable, safe, and suitable for production use in home servers, homelabs, and small office environments. 
It focuses on reliability and does not modify any Samba settings outside the user’s explicit actions.

