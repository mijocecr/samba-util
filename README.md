
---
<img width="630" height="738" alt="image" src="https://github.com/user-attachments/assets/52f7b4f3-2773-4e81-b5cd-04b74430b42c" />

https://wiki.archlinux.org/title/Samba-util-bin
---

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

### Printing
- Enable or disable Samba printing backed by CUPS
- Adds `[printers]` and `[print$]` only when they are missing
- Marks managed changes so they can be reverted without touching custom sections

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
yay -S samba-util-bin
```
Install with `paru`:


```bash
paru -S samba-util-bin
```

Manual AUR clone:

```bash
git clone https://aur.archlinux.org/samba-util-bin.git
cd samba-util-bin
makepkg -si
```

---

## Running SAMBA‑Util

### From source:
```bash
git clone https://github.com/mijocecr/samba-util.git
cd samba-util
dotnet run
```

When started from an interactive terminal without a graphical display, SAMBA-Util opens its numbered CLI. The CLI provides the same management areas as the graphical app: shares and filesystem permissions, Samba users, network discovery and remote shares, status and service actions, configuration (smb.conf path, default permissions, minimum SMB protocol for client and server, CUPS printing) and UFW/firewalld, logs, active connections, and Samba version. At every selection prompt, choose from the options printed on screen; share and user operations use numbered lists rather than requiring you to remember or retype existing names. Administrative actions require sudo authentication at startup.

## Requirements

- Linux distribution with `systemd`
-  `samba` 
- `linux-headers` needed for kernel controller 
- `cifs-utils`    needed to operate the filesystem
- `gvfs-backends` -> (Debian/Ubuntu) needed to operate the filesystem
- `gvfs-fuse` -> (Debian/Ubuntu) needed to operate the filesystem
- `smbclient` -> (Debian/Ubuntu) needed to operate samba 
- `cups` -> optional, required to share printers through Samba
- .NET 10.0 runtime (or self‑contained build)  
- `sudo` privileges for administrative actions  
- Access to `/etc/samba/smb.conf`

  For all Arch based distros run `sudo pacman -S --needed gvfs gvfs-smb kio-extras samba smbclient cifs-utils xdg-utils` if you have problems with dependencies.

---

## License

SAMBA‑Util is distributed under the **MIT License**, allowing free use, modification, and distribution.

---
Project Status
---
SAMBA‑Util is stable, safe, and suitable for production use in home servers, homelabs, and small office environments. 
It focuses on reliability and does not modify any Samba settings outside the user’s explicit actions.

---

<img width="1536" height="1024" alt="image" src="https://github.com/user-attachments/assets/4568afa6-b8fb-4dab-9817-7eb99f1b68fd" />


---

 Youtube Video
---
**https://youtu.be/tZ6jXj-cL4Q**
