# Base image used to bundle the AppImage runtime libraries.
#
# AlmaLinux 9 is used on purpose: its glibc (2.34) is older than the one in
# current rolling distributions, so the libraries copied out of this image do
# not force a newer glibc on the target system. It ships OpenSSL 3, which the
# .NET 10 runtime requires.
#
# The image only needs to *contain* the libraries; the application itself is
# built on the host and mounted read-only when bundling.
FROM almalinux:9

RUN dnf -y install --setopt=install_weak_deps=False \
        openssl-libs \
        libicu \
        libX11 libXext libXcursor libXfixes libXi libXrandr libXrender \
        libXau libXdmcp \
        libICE libSM \
        libuuid \
        lttng-ust \
    && dnf clean all \
    && rm -rf /var/cache/dnf

# Refresh the loader cache so `ldconfig -p` lists everything we just installed.
RUN ldconfig
