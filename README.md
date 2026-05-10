# SAMBA‑Util

SAMBA‑Util is a graphical application designed to simplify the management of Samba shares, users, and configuration files on Linux systems.  
It provides a clear and accessible interface that allows administrators to configure Samba without manually editing `smb.conf` or relying on complex command‑line tools.

The goal of the project is to offer a dependable and easy‑to‑use utility that makes Samba administration more intuitive, organized, and safe for both new and experienced users.

---

## Purpose

Samba is widely used for file sharing across networks, but its configuration can be difficult to manage efficiently through text files or terminal commands.  
SAMBA‑Util presents these capabilities through a modern interface that emphasizes clarity, consistency, and ease of use, helping users avoid common mistakes and streamline their workflow.

---

## Key Features

### Share Management
- Create, edit, and remove Samba shares  
- Configure paths, permissions, masks, and visibility options  
- Validate share names and paths before saving  
- Browse and detect available SMB folders on the network  
- Reload Samba configuration safely and without service interruption  

### User Management
- Add and remove Samba users  
- Synchronize system accounts with Samba  
- Handle passwords securely  
- Detect invalid or outdated credentials  

### System Integration
- Read and write `/etc/samba/smb.conf` in a controlled and safe manner  
- Perform administrative actions using `sudo`  
- Detect incorrect administrator passwords without affecting system authentication  
- Display Samba service status and relevant logs  

### Modern Interface
- Built with Avalonia for a consistent cross‑platform experience  
- Clean, organized layout with clear sections  
- Support for SVG and PNG icons  
- Designed for readability and simplicity  

---

## Installation

For all Linux distributions there is an AppImage available and

Pre‑built packages on the project’s GitHub page:

**https://github.com/mijocecr/samba-util/releases**


### AUR (Arch Linux / Manjaro / EndeavourOS)

SAMBA‑Util is available in the Arch User Repository.

Install with `yay`:

```bash
yay -S samba-util
```
Install with `paru`:


```bash
paru -S samba-util
```

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
git clone https://github.com/mijocecr/samba-util.git
cd samba-util
dotnet run
```
---
## License

SAMBA‑Util is distributed under the **MIT License**, allowing free use, modification, and distribution.

---
Project Status
---
SAMBA‑Util is stable, safe, and suitable for production use in home servers, homelabs, and small office environments. 
It focuses on reliability and does not modify any Samba settings outside the user’s explicit actions.

---

<img width="3840" height="2160" alt="banner_01" src="https://github.com/user-attachments/assets/390f0b06-643b-4986-9d8c-84f1ee5d1c89" />

---


