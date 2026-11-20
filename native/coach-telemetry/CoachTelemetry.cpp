#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stddef.h>
#include <string.h>
#include "sdk/scssdk_telemetry.h"

namespace {
struct CoachFrame {
    LONG sequence;
    DWORD version;
    DWORD active;
    DWORD plannedKm;
    char origin[96];
    char destination[96];
    char destinationId[96];
};
static_assert(sizeof(CoachFrame) == 304);

HANDLE mapping;
CoachFrame* shared;

void publish(const CoachFrame& next)
{
    InterlockedIncrement(&shared->sequence);
    constexpr auto offset = offsetof(CoachFrame, version);
    memcpy(reinterpret_cast<char*>(shared) + offset,
                reinterpret_cast<const char*>(&next) + offset, sizeof(next) - offset);
    InterlockedIncrement(&shared->sequence);
}

void copyText(char (&target)[96], const scs_named_value_t& value)
{
    if (value.value.type != SCS_VALUE_TYPE_string || !value.value.value_string.value) return;
    const char* text = value.value.value_string.value;
    for (size_t i = 0; i < sizeof(target) - 1 && text[i]; ++i) target[i] = text[i];
}

SCSAPI_VOID configuration(scs_event_t, const void* info, scs_context_t)
{
    if (!shared || !info) return;
    const auto& config = *static_cast<const scs_telemetry_configuration_t*>(info);
    if (strcmp(config.id, "bus_job") != 0) return;

    CoachFrame next{};
    next.version = 1;
    next.active = config.attributes && config.attributes->name ? 1 : 0;
    for (auto item = config.attributes; item && item->name; ++item) {
        if (!next.origin[0] && (strcmp(item->name, "source_city") == 0 ||
                               strcmp(item->name, "source_stop.name") == 0)) copyText(next.origin, *item);
        else if (!next.destination[0] && (strcmp(item->name, "destination_city") == 0 ||
                                         strcmp(item->name, "destination_stop.name") == 0)) copyText(next.destination, *item);
        else if (strcmp(item->name, "destination_city_id") == 0) copyText(next.destinationId, *item);
        else if (strcmp(item->name, "planned_distance_km") == 0 && item->value.type == SCS_VALUE_TYPE_u32)
            next.plannedKm = item->value.value_u32.value;
    }
    publish(next);
}
}

extern "C" SCSAPI_VOID scs_telemetry_shutdown()
{
    if (shared) {
        CoachFrame empty{};
        empty.version = 1;
        publish(empty);
        UnmapViewOfFile(shared);
        shared = nullptr;
    }
    if (mapping) {
        CloseHandle(mapping);
        mapping = nullptr;
    }
}

extern "C" SCSAPI_RESULT scs_telemetry_init(
    scs_u32_t version, const scs_telemetry_init_params_t* const params)
{
    if (version != SCS_TELEMETRY_VERSION_1_01) return SCS_RESULT_unsupported;
    const auto* init = static_cast<const scs_telemetry_init_params_v101_t*>(params);
    if (strcmp(init->common.game_id, "eut2") != 0) return SCS_RESULT_unsupported;
    mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(CoachFrame),
                                 L"Local\\ETS2DiscordPresence.Coaches");
    if (!mapping) return SCS_RESULT_generic_error;
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        scs_telemetry_shutdown();
        return SCS_RESULT_generic_error;
    }
    shared = static_cast<CoachFrame*>(MapViewOfFile(mapping, FILE_MAP_WRITE, 0, 0, sizeof(CoachFrame)));
    if (!shared) {
        scs_telemetry_shutdown();
        return SCS_RESULT_generic_error;
    }
    CoachFrame empty{};
    empty.version = 1;
    publish(empty);
    const auto registered = init->register_for_event(SCS_TELEMETRY_EVENT_configuration, configuration, nullptr);
    if (registered != SCS_RESULT_ok) scs_telemetry_shutdown();
    return registered;
}
