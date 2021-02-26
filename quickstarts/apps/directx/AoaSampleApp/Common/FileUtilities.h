// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#pragma once

#include <algorithm>
#include <cstdio>
#include <string>
#include <memory>
#include <vector>

#include <PathCch.h>
#include <winrt\Windows.Foundation.h>

namespace AoaSampleApp
{
    inline std::string GetFilenameExtension(const std::string& filename)
    {
        auto pos = filename.find_last_of('.');
        if (pos == std::string::npos || pos + 1 == filename.size())
            return std::string();
        else
            return filename.substr(pos + 1, filename.size() - pos - 1);
    }

    inline std::string RemoveFilenameExtension(const std::string& filename)
    {
        auto pos = filename.find_last_of('.');
        if (pos == std::string::npos)
            return filename;
        else
            return filename.substr(0, pos);
    }

    inline std::string GetFilenamePath(const std::string& filename)
    {
        auto pos = filename.find_last_of('\\');
        if (pos == std::string::npos)
            pos = filename.find_last_of('/');

        if (pos == std::string::npos)
            return std::string();
        else
            return filename.substr(0, pos);
    }

    inline std::wstring StringToWideString(const std::string& sourceString)
    {
        size_t destinationSizeInWords = sourceString.length() + 1;
        std::unique_ptr<wchar_t[]> wideString(new wchar_t[destinationSizeInWords]);
        size_t nConverted = 0;
        mbstowcs_s(&nConverted, wideString.get(), destinationSizeInWords, sourceString.data(), _TRUNCATE);
        return std::wstring(wideString.get());
    }

    inline std::string WideStringToString(const std::wstring& sourceString)
    {
        size_t destinationSizeInBytes = (sourceString.length() + 1) * 4;
        std::unique_ptr<char[]> narrowString(new char[destinationSizeInBytes]);
        size_t nConverted = 0;
        wcstombs_s(&nConverted, narrowString.get(), destinationSizeInBytes, sourceString.data(), _TRUNCATE);
        return std::string(narrowString.get());
    }

    inline std::string GetExecutablePath()
    {
        wchar_t wideExecutableFilename[MAX_PATH];
        GetModuleFileNameW(nullptr, wideExecutableFilename, MAX_PATH);
        return GetFilenamePath(WideStringToString(wideExecutableFilename));
    }

    inline FILE* OpenFile(const std::string& filename, const char* mode)
    {
        std::wstring wideFilename = StringToWideString(filename);
        std::wstring wideMode = StringToWideString(std::string(mode));

        FILE* pFile = nullptr;

        // First try the filename as-is
        _wfopen_s(&pFile, wideFilename.c_str(), wideMode.c_str());
        if (pFile)
            return pFile;

        // Then try the filename relative to the executable location
        std::wstring exePath = StringToWideString(GetExecutablePath());
        _wfopen_s(&pFile, (exePath + L"/" + wideFilename).c_str(), wideMode.c_str());
        if (pFile)
            return pFile;

        return nullptr;
    }

    inline bool FileExists(const std::string& filename)
    {
        FILE* pFile = OpenFile(filename, "rb");

        if (pFile)
        {
            fclose(pFile);
            return true;
        }
        else
        {
            return false;
        }
    }

    static inline std::string FormatDateTime(time_t const& t)
    {
        struct tm tm;
        localtime_s(&tm, &t);

        std::ostringstream oss;
        oss << std::put_time(&tm, "%Y%m%d-%H%M%S");

        return oss.str();
    }

    static inline std::wstring PathJoin(std::wstring const& folder, std::wstring const& filename)
    {
        WCHAR fullPath[512] = { 0 };

        winrt::check_hresult(
            PathCchCombineEx(
                fullPath,
                _countof(fullPath),
                folder.c_str(),
                filename.c_str(),
                PATHCCH_ALLOW_LONG_PATHS));

        return fullPath;
    }

    static inline std::wstring PathDirectory(const std::wstring& filename)
    {
        std::vector<wchar_t> buffer(filename.length() + 1, L'\0');
        std::copy(filename.cbegin(), filename.cend(), buffer.begin());

        winrt::check_hresult(
            PathCchRemoveFileSpec(buffer.data(), buffer.size()));

        return buffer.data();
    }

    static inline std::wstring PathFilename(const std::wstring& filename)
    {
        auto pos = filename.find_last_of(L'\\');
        if (pos == std::wstring::npos)
        {
            pos = filename.find_last_of(L'/');
        }

        if (pos == std::wstring::npos)
        {
            return filename;
        }
        else
        {
            return filename.substr(pos + 1, filename.length() - pos - 1);
        }
    }
}
