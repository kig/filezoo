#!/bin/sh
mkdir -p bin;
gmcs -pkg:gtk-sharp-2.0 -r:Mono.Cairo -r:Mono.Posix -out:bin/filezoo.exe src/*.cs

