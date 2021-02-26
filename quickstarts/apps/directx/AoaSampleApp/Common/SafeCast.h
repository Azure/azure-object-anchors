// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#pragma once

namespace AoaSampleApp
{
    template <typename T, typename U>
    T& AsRef(U& u)
    {
        static_assert(sizeof(u) == sizeof(T), "Referenced types are not the same size.");
        return reinterpret_cast<T&>(u);
    }

    template <typename T, typename U>
    T const& AsRef(U const& u)
    {
        static_assert(sizeof(u) == sizeof(T), "Referenced types are not the same size.");
        return reinterpret_cast<T const&>(u);
    }
}
