/*++

Copyright (c) Microsoft Corporation.  All rights reserved.

    THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
    KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
    IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR
    PURPOSE.

ModuleName:

    public.h

Abstract:

    Contains definitions used both by driver and application

    Modified for Pods: the AAP channel's fixed PSM and Pods' own device
    interface GUID replace the echo sample's service GUID and interface.

--*/

#pragma once

//
// Apple's accessory protocol (AAP) listens on this fixed L2CAP PSM. The
// AirPods' SDP record advertises the AAP service UUID
// 74ec2172-0bad-4d01-8f77-997b2be0722a, which the INF matches.
//
#define PODSAAP_PSM 0x1001

//
// Device interface exposed by our bth client device (Pods' own GUID)
//

/* 496c4781-b0e8-48e3-ae88-6f645f4b809a */
DEFINE_GUID(BTHECHOSAMPLE_DEVICE_INTERFACE, 0x496c4781, 0xb0e8, 0x48e3, 0xae, 0x88, 0x6f, 0x64, 0x5f, 0x4b, 0x80, 0x9a);

