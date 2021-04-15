#!/usr/bin/env bash

dotnet contrib/depotdownloader-2.3.6/DepotDownloader.dll -app 896660 -os linux -filelist .depot-downloader-file-list.txt -dir serverfiles
