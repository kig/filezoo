#!/bin/sh
gmcs -debug -t:library -langversion:linq -pkg:nunit -pkg:gnome-sharp-2.0 -pkg:gnome-vfs-sharp-2.0 -pkg:gtk-sharp-2.0 -r:Mono.Cairo -r:Mono.Posix -out:test/filezoo.dll src/*.cs test/*.cs
