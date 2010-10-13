#!/bin/sh
mkdir -p bin &&
gmcs -pkg:gnome-sharp-2.0 -pkg:gnome-vfs-sharp-2.0 -pkg:gtk-sharp-2.0 -r:Mono.Cairo -r:Mono.Posix -out:bin/filezoo.exe src/*.cs
# && mono --aot -O=all bin/filezoo.exe > /dev/null
