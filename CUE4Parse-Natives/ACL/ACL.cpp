#include "includes/ACLDecompress.h"

// Forward declaration
template <bool bUseBindPose>
void ProcessTracks(const acl::compressed_tracks& tracks, FTransform* inRefPoses, FTrackToSkeletonMap* inTrackToSkeletonMap, FTransform* outAtom);

// ACL allocator
DLLEXPORT void* nAllocate(size_t size, size_t alignment) { return ACLAllocatorImpl.allocate(size, alignment); }
DLLEXPORT void nDeallocate(void* ptr, size_t size) { ACLAllocatorImpl.deallocate(ptr, size); }

// ACL compressed tracks
DLLEXPORT const char* nCompressedTracks_IsValid(acl::compressed_tracks* tracks, bool checkHash) { return tracks->is_valid(checkHash).c_str(); }
DLLEXPORT void nTracksHeader_SetDefaultScale(acl::acl_impl::tracks_header* header, uint32_t defaultScale) { header->set_default_scale(defaultScale); }

DLLEXPORT void nReadACLData(const acl::compressed_tracks& tracks, FTransform* inRefPoses, FTrackToSkeletonMap* inTrackToSkeletonMap, FTransform* outAtom)
{
    // Always resolve stripped ("default") sub-tracks with identity values, matching UE's ACL plugin.
    // UE's decompression writer (FUE4OutputWriter in ACLDecompressionImpl.h) does NOT override the
    // default-sub-track modes, so it inherits acl::track_writer's base modes: rotation/translation =
    // `constant` (identity quat / zero vector), scale = `legacy` (uses get_default_scale()). UE-compressed
    // clips only strip a sub-track as DEFAULT when its value equals that identity default; any non-zero bind
    // offset is stored explicitly as a CONSTANT sub-track. The bUseBindPose=true / `variable` path resolves
    // defaults to the *ref/bind pose* instead, which mis-anchors bones whose animated value is exactly
    // identity (e.g. Loki's body-baked Scepter_Root, keyed to local-zero so it sits at weapon_r/the hand,
    // was placed at its 110cm bind offset = his feet). ProcessTracks<false> == FUE4OutputWriter's modes.
    // (The <true> path is kept below for reference but is no longer selected.)
    ProcessTracks<false>(tracks, inRefPoses, inTrackToSkeletonMap, outAtom);
}

DLLEXPORT void nReadCurveACLData(const acl::compressed_tracks& tracks, float* outFloatKeys)
{
    uint32_t numSamples = tracks.get_num_samples_per_track();
    float sampleRate = tracks.get_sample_rate();
    float duration = tracks.get_finite_duration();

    DecompContextDefault context;
    context.initialize(tracks);

    FCUE4ParseCurveWriter writer(outFloatKeys, numSamples);
    for (uint32_t sampleIndex = 0; sampleIndex < numSamples; ++sampleIndex)
    {
        const float sample_time = rtm::scalar_min(float(sampleIndex) / sampleRate, duration);
        context.seek(sample_time, acl::sample_rounding_policy::nearest);
        writer.SampleIndex = sampleIndex;
        context.decompress_tracks(writer);
    }
}

template <bool bUseBindPose>
void ProcessTracks(const acl::compressed_tracks& tracks, FTransform* inRefPoses, FTrackToSkeletonMap* inTrackToSkeletonMap, FTransform* outAtom)
{
    uint32_t numSamples = tracks.get_num_samples_per_track();
    float sampleRate = tracks.get_sample_rate();
    float duration = tracks.get_finite_duration();

    DecompContextDefault context;
    context.initialize(tracks);

    FCUE4ParseOutputWriter<bUseBindPose> writer(inRefPoses, inTrackToSkeletonMap, outAtom, numSamples);
    for (uint32_t sampleIndex = 0; sampleIndex < numSamples; ++sampleIndex)
    {
        const float sample_time = rtm::scalar_min(float(sampleIndex) / sampleRate, duration);
        context.seek(sample_time, acl::sample_rounding_policy::nearest);
        writer.SampleIndex = sampleIndex;
        context.decompress_tracks(writer);
    }
}
