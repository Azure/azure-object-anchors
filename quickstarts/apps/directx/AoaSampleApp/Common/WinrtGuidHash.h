// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#pragma once

#include <winrt/base.h>

// Hash specialization for GUIDs so we can use them in unordered_maps.
namespace std
{
    template<>
    struct hash <winrt::guid>
    {
        typedef winrt::guid argument_type;
        typedef std::size_t result_type;

        result_type operator()(argument_type const& guid) const
        {
            // This is the hash algorithm for ST::GUID above.
            return static_cast<result_type>((guid.Data1 ^ ((guid.Data2 << 16) | guid.Data3)) ^ ((guid.Data4[2] << 24) | guid.Data4[7]));
        }
    };
}
