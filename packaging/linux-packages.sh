#!/usr/bin/env bash
# Build satr_<ver>_amd64.deb and satr-<ver>-1-x86_64.pkg.tar.zst from a linux-x64 publish tree.
# Usage: linux-packages.sh [version] [publish-dir] [output-dir]
set -euo pipefail
packaging="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd -- "$packaging/.." && pwd)"
version="${1:-$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$root/src/Satr/Satr.csproj" | head -n1)}"
publish="${2:-$root/artifacts/linux-x64}"
out="${3:-$root/artifacts}"
deb_name="satr_${version}_amd64.deb"
pkg_name="satr-${version}-1-x86_64.pkg.tar.zst"

if ! command -v dpkg-deb >/dev/null || ! command -v makepkg >/dev/null; then
    printf 'skip linux packages: need dpkg-deb and makepkg\n'
    exit 0
fi
test -x "$publish/Satr" || { printf 'missing %s/Satr\n' "$publish" >&2; exit 1; }
mkdir -p -- "$out"
stage="$out/pkg-staging"
rm -rf -- "$stage"
mkdir -p -- "$stage"

payload() {
    tar -C "$publish" --exclude=install-linux.sh --exclude=uninstall-linux.sh -cf - .
}

# --- .deb ---
deb_root="$stage/deb/satr_${version}_amd64"
mkdir -p -- "$deb_root/DEBIAN" "$deb_root/usr/lib/satr" "$deb_root/usr/bin" \
    "$deb_root/usr/share/applications" "$deb_root/usr/share/doc/satr"
payload | tar -C "$deb_root/usr/lib/satr" -xf -
chmod 755 -- "$deb_root/usr/lib/satr/Satr"
ln -s /usr/lib/satr/Satr "$deb_root/usr/bin/satr"
install -m644 "$packaging/satr.desktop" "$deb_root/usr/share/applications/satr.desktop"
install -m644 "$packaging/satr-workspace.desktop" "$deb_root/usr/share/applications/satr-workspace.desktop"
install -m644 "$root/LICENSE" "$deb_root/usr/share/doc/satr/copyright"
size="$(du -sk "$deb_root" | cut -f1)"
cat > "$deb_root/DEBIAN/control" <<EOF
Package: satr
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Installed-Size: $size
Depends: libx11-6, libice6, libsm6, libxrandr2, libxi6, libxcursor1, libfontconfig1, libfreetype6, libssl3, zlib1g, libicu76 | libicu74 | libicu72 | libicu70
Maintainer: Mohamad Salah <98090972+Mo999salah@users.noreply.github.com>
Homepage: https://github.com/Mo999salah/Satr
Description: Project workspace terminal for AI coding CLIs
 Satr is a desktop terminal for Codex, Claude Code, and other AI CLIs,
 with mixed Arabic/English rendering. This package ships a self-contained
 .NET build. Install AI CLIs separately.
EOF
find "$deb_root" -type d -exec chmod 755 {} +
chmod 644 -- "$deb_root/DEBIAN/control"
dpkg-deb --root-owner-group --build "$deb_root" "$out/$deb_name"

# --- Arch / CachyOS ---
arch="$stage/arch"
mkdir -p -- "$arch"
payload | gzip -n > "$arch/payload.tar.gz"
install -m644 "$packaging/satr.desktop" "$arch/satr.desktop"
install -m644 "$packaging/satr-workspace.desktop" "$arch/satr-workspace.desktop"
cat > "$arch/PKGBUILD" <<EOF
pkgname=satr
pkgver=$version
pkgrel=1
pkgdesc='Project workspace terminal for AI coding CLIs'
arch=('x86_64')
url='https://github.com/Mo999salah/Satr'
license=('MIT')
depends=('libx11' 'libice' 'libsm' 'libxrandr' 'libxi' 'libxcursor' 'fontconfig' 'freetype2' 'icu' 'openssl' 'zlib')
options=('!strip' '!debug')
source=('payload.tar.gz' 'satr.desktop' 'satr-workspace.desktop')
sha256sums=('SKIP' 'SKIP' 'SKIP')

package() {
  install -d "\$pkgdir/usr/lib/satr"
  find "\$srcdir" -mindepth 1 -maxdepth 1 ! -name satr.desktop ! -name satr-workspace.desktop -exec cp -a {} "\$pkgdir/usr/lib/satr/" \\;
  chmod 755 "\$pkgdir/usr/lib/satr/Satr"
  install -d "\$pkgdir/usr/bin"
  ln -s /usr/lib/satr/Satr "\$pkgdir/usr/bin/satr"
  install -Dm644 "\$srcdir/satr.desktop" "\$pkgdir/usr/share/applications/satr.desktop"
  install -Dm644 "\$srcdir/satr-workspace.desktop" "\$pkgdir/usr/share/applications/satr-workspace.desktop"
  install -Dm644 "\$pkgdir/usr/lib/satr/LICENSE" "\$pkgdir/usr/share/licenses/\$pkgname/LICENSE"
}
EOF
(cd "$arch" && makepkg -f --noconfirm)
shopt -s nullglob
built=("$arch"/satr-"$version"-*.pkg.tar.zst "$arch"/satr-"$version"-*.pkg.tar.xz)
test "${#built[@]}" -eq 1 || { printf 'expected one Arch package, got %s\n' "${#built[@]}" >&2; exit 1; }
cp -f -- "${built[0]}" "$out/$pkg_name"

if command -v repo-add >/dev/null; then
    repo="$stage/pacman-repo"
    mkdir -p -- "$repo"
    cp -f -- "$out/$pkg_name" "$repo/"
    repo-add --nocolor "$repo/satr.db.tar.zst" "$repo/$pkg_name"
    # GitHub Releases cannot store symlinks; pacman fetches satr.db by name.
    cp -L -- "$repo/satr.db" "$out/satr.db"
    cp -L -- "$repo/satr.db.tar.zst" "$out/satr.db.tar.zst"
    cp -L -- "$repo/satr.files" "$out/satr.files"
    cp -L -- "$repo/satr.files.tar.zst" "$out/satr.files.tar.zst"
fi

# --- check ---
dpkg-deb -I "$out/$deb_name" | grep -q 'Package: satr'
tar -tf "$out/$pkg_name" | grep -qx 'usr/bin/satr'
if tar -tf "$out/$pkg_name" | grep -q 'install-linux.sh'; then
    printf 'install-linux.sh leaked into %s\n' "$pkg_name" >&2
    exit 1
fi
printf 'linux packages: %s %s\n' "$out/$deb_name" "$out/$pkg_name"
