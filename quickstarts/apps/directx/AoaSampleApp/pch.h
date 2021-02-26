// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#pragma once

#include <algorithm>
#include <array>
#include <chrono>
#include <ctime>
#include <d2d1_2.h>
#include <d3d11_4.h>
#include <DirectXCollision.h>
#include <DirectXColors.h>
#include <DirectXMath.h>
#include <dwrite_2.h>
#include <functional>	// For bind
#include <future>
#include <iomanip>
#include <mutex>
#include <numeric>
#include <sstream>
#include <unordered_map>
#include <vector>
#include <thread>
#include <time.h>
#include <wincodec.h>
#include <WindowsNumerics.h>

#include <Windows.Graphics.Directx.Direct3D11.Interop.h>
#include <wrl/client.h>

#include <winrt/Windows.ApplicationModel.Activation.h>
#include <winrt/Windows.ApplicationModel.Core.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Foundation.Metadata.h>
#include <winrt/Windows.Gaming.Input.h>
#include <winrt/Windows.Graphics.Display.h>
#include <winrt/Windows.Graphics.Holographic.h>
#include <winrt/Windows.Perception.People.h>
#include <winrt/Windows.Perception.Spatial.h>
#include <winrt/Windows.Perception.Spatial.Preview.h>
#include <winrt/Windows.Security.Authorization.AppCapabilityAccess.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Input.Spatial.h>

#include "Common/FileUtilities.h"
#include "Common/SafeCast.h"
#include "Common/WinrtGuidHash.h"
