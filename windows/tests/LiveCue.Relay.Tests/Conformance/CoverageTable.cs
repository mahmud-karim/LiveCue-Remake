using System.Reflection;
using System.Text;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>
/// One row of the coverage table: a statement of protocol section 15 (side R) or an item of the security
/// list (test plan section 4), the tests that prove it through a real loopback listener, the tests that
/// prove it below the listener, and a note where one is needed. A test is "Folder.Class.Method" inside
/// this test project; a test of another project is written in round brackets and is not resolved.
/// </summary>
internal sealed record CoverageRow(string Id, string[] Through, string[] Below, string Note = "")
{
    public bool IsStatement => Id.StartsWith("P2-", StringComparison.Ordinal);
}

/// <summary>
/// The coverage table the test plan asks of WP-W13 (W-design 6.4: "every P2 statement of side R has a
/// passing test through a real loopback listener; every item of the security list passes"). It is data,
/// so that it cannot drift: <see cref="CoverageTableTests"/> resolves every test it names, and
/// <c>Coverage.md</c> beside this file is its rendering.
/// </summary>
internal static class CoverageTable
{
    private const string E2E = "Conformance.EndToEndRouteTests.Each_route_once_with_valid_input";
    private const string PrivateSweep = "Conformance.PrivateTextSweepTests.No_response_frame_or_status_contains_private_text";
    private const string TokenSweep = "Conformance.TokenSweepTests.No_event_notice_or_diagnostic_of_a_full_run_contains_the_token_or_an_authorization_value";
    private const string ResetSweep = "Conformance.ResetSweepTests.After_a_reset_the_old_token_fails_on_http_and_on_the_upgrade";
    private const string PauseSweep = "Conformance.PauseSweepTests.Paused_refuses_routes_5_to_8_and_serves_1_2_3_4_and_9";
    private const string StopSweep = "Conformance.StopSweepTests.Stop_ends_a_hanging_run_and_an_open_stream_within_the_budget_and_frees_the_port";
    private const string LeaseSweep = "Conformance.LeaseSweepTests.An_open_stream_refuses_stop_and_reports_busy_then_stop_works";
    private const string UsualRun = "Conformance.InjectionTests.Whatever_else_a_request_holds_the_process_is_started_as_always";
    private const string Sequence = "Conformance.LocalModelStatementTests.P2_080_to_P2_085_the_sequence_of_the_statements";
    private const string Stored = "Conformance.PairingStatementTests.P2_140_P2_141_P2_142_the_stored_pairing_and_the_shown_payload";
    private const string HostileOutput = "Conformance.AnswerSecurityTests.Hostile_output_cases";
    private const string Offered = "Conformance.CatalogueStatementTests.P2_038_P2_039_P2_040_the_offered_list_is_the_filtered_one";

    public static IReadOnlyList<CoverageRow> Rows { get; } =
    [
        // Protocol 15.1: edge.
        new("P2-001", ["Edge.UnauthorizedTests.No_authorization_is_the_fixed_401_on_any_method_and_target", "Conformance.EdgeSecurityTests.The_three_401_answers_are_identical"], []),
        new("P2-002", ["Edge.UnauthorizedTests.A_well_formed_unknown_token_is_the_same_401", "Conformance.EdgeSecurityTests.An_unauthenticated_caller_sees_the_same_bytes_whatever_the_relay_is_doing"], []),
        new("P2-003", ["Edge.UnauthorizedTests.Browser_headers_are_the_same_401_on_every_route_and_on_the_upgrade", "Conformance.EdgeSecurityTests.A_browser_header_alone_with_any_value_is_the_fixed_401_on_every_route"], []),
        new("P2-004", ["Edge.UnauthorizedTests.Malformed_authorization_is_the_same_401", "Edge.BearerCheckTests.A_token_with_a_trailing_line_break_is_the_fixed_401"], []),
        new("P2-005", ["Edge.UnauthorizedTests.A_401_does_no_work", "Conformance.EdgeSecurityTests.A_401_touches_nothing"], []),
        new("P2-006", ["Edge.RouteMatchTests.Unknown_targets_are_404", "Conformance.EdgeSecurityTests.Odd_targets_and_methods_are_404", "Conformance.EdgeSecurityTests.No_control_route_exists"], []),
        new("P2-007", ["Edge.ProtocolHeaderTests.Absent_or_other_version_is_400_and_an_unknown_route_stays_404", "Conformance.EdgeSecurityTests.A_repeated_protocol_header_is_a_mismatch"], []),
        new("P2-008", ["Edge.ResponseHeaderTests.Every_response_has_only_the_allowed_headers", "Conformance.EndToEndRouteTests.Every_answer_of_a_full_relay_has_only_the_allowed_headers"], []),
        new("P2-009", ["Edge.BodyRuleTests.Get_and_delete_with_a_body_are_400"], []),
        new("P2-010", ["Edge.RequestGaugeTests.One_request_beyond_the_cap_is_429_and_a_finished_one_frees_a_place", "Conformance.CapTests.One_request_beyond_the_gauge_is_429_whatever_route_it_names"], []),
        new("P2-011", ["Edge.ErrorBodyTests.Every_error_is_an_object_with_error_and_a_listed_code", PrivateSweep], []),
        new("P2-012", ["Edge.BodyLimitTests.One_byte_over_is_413_with_a_length_and_chunked_and_the_limit_itself_passes", "Conformance.BodySecurityTests.A_lying_length_cannot_exceed_the_cap", "Conformance.EmojiRoundTripTests.A_request_of_emoji_at_the_body_limit_is_served_and_arrives_whole"], []),
        new("P2-013", ["Edge.BodyRuleTests.Non_object_and_empty_bodies_are_400_on_every_body_route", "Conformance.BodySecurityTests.Hostile_json_cases"], []),
        new("P2-014", ["Edge.BodyTimeoutTests.A_body_that_stalls_is_408_and_frees_what_it_held", "Conformance.SlowClientTests.A_stalled_body_releases_everything"], []),
        new("P2-015", ["Edge.ListenerTests.The_only_endpoint_is_loopback"], []),
        new("P2-016", ["Edge.FunnelHeaderTests.Private_refuses_and_public_serves_a_funnel_marked_request", "Conformance.EdgeSecurityTests.A_funnel_marked_request_follows_the_mode_on_every_route"], []),
        new("P2-017", ["Edge.PresenceTests.One_event_per_authenticated_request_and_none_for_the_current_nonce", "Conformance.EdgeSecurityTests.The_self_check_header_is_no_credential_and_a_wrong_nonce_is_a_phone"], []),
        new("P2-018", ["Edge.DeadlineTests.Work_past_the_route_deadline_is_504"], [], "Through the real edge and listener with a handler that never ends (EdgeHarness), as the test plan says."),
        new("P2-019", ["Edge.EdgeStatisticsTests.A_thousand_of_each_kind_give_exact_counts", "Edge.EdgeStatisticsTests.At_most_one_event_per_interval_and_none_without_a_change", TokenSweep], []),

        // Protocol 15.2: health and pairing verification.
        new("P2-020", ["Edge.HealthTests.Exactly_three_members_also_while_paused", E2E], []),
        new("P2-021", ["Edge.PairVerifyTests.Any_object_is_ok_also_while_paused_and_records_nothing", TokenSweep], []),

        // Protocol 15.3: catalogue and selection.
        new("P2-030", ["Conformance.CatalogueStatementTests.P2_030_five_reads_and_five_validations_start_one_catalogue_process"], ["Catalog.CachedCatalogSourceTests.Five_reads_run_the_loader_once"]),
        new("P2-031", ["Conformance.CatalogueStatementTests.P2_031_P2_032_a_failing_read_returns_the_last_good_catalogue_unchanged"], ["Catalog.CatalogReaderTests.A_failing_loader_returns_the_last_good_catalogue"]),
        new("P2-032", ["Conformance.CatalogueStatementTests.P2_031_P2_032_a_failing_read_returns_the_last_good_catalogue_unchanged"], ["Catalog.CatalogReaderTests.Changing_a_returned_catalogue_does_not_change_the_next_read"], "The statement is about a result object a caller in the process changes; no request can do that. Through the listener: the catalogue served after the failure is byte for byte the one served before."),
        new("P2-033", ["Conformance.CatalogueStatementTests.P2_033_an_empty_catalogue_is_served_empty_and_validates_nothing"], ["Catalog.CatalogReaderTests.An_empty_catalogue_replaces_the_last_good_one"]),
        new("P2-034", ["Conformance.CatalogueStatementTests.P2_034_without_any_good_catalogue_there_is_no_fallback_list"], ["Catalog.CatalogReaderTests.Without_any_good_catalogue_the_result_is_unavailable"]),
        new("P2-035", ["Conformance.CatalogueStatementTests.P2_035_one_failure_then_success_takes_two_loader_calls"], ["Catalog.CatalogReaderTests.One_failure_then_success_takes_two_loader_calls"]),
        new("P2-036", ["Catalog.ModelsRouteTests.A_failing_catalogue_is_503_with_the_code"], []),
        new("P2-037", ["Answers.AssistRouteTests.A_failing_catalogue_is_503_and_the_backend_does_not_run"], []),
        new("P2-038", [Offered], ["Catalog.OfferFilterTests.Always_none_models_get_none_first"]),
        new("P2-039", [Offered], ["Catalog.OfferFilterTests.Other_models_keep_only_known_efforts"]),
        new("P2-040", [Offered], ["Catalog.OfferFilterTests.Hidden_bad_id_and_effortless_entries_are_not_offered_and_a_nameless_one_uses_its_id"]),
        new("P2-041", ["Conformance.CatalogueStatementTests.P2_041_the_validation_cases_of_the_statement", "Conformance.InjectionTests.No_request_text_reaches_an_argument"], ["Catalog.AssistantSelectionTests.Validation_cases"]),
        new("P2-042", ["Answers.AssistRouteTests.An_unknown_model_is_selection_unavailable"], []),
        new("P2-043", ["Answers.AssistRouteTests.A_missing_assistant_is_invalid_on_both_routes_and_nothing_runs"], []),
        new("P2-044", ["Conformance.CatalogueStatementTests.P2_044_the_catalogue_exchange_with_a_real_process"], ["Catalog.CodexCatalogSourceTests.The_exchange_writes_the_three_lines", "Catalog.CodexCatalogProcessTests.The_exchange_works_against_a_stub_process_and_the_process_is_gone_afterwards"]),

        // Protocol 15.4: assist, summary, cancel.
        new("P2-050", ["Answers.AssistRouteTests.A_valid_backend_answer_is_returned", E2E], []),
        new("P2-051", [PauseSweep, "Answers.AnswerPauseTests.Paused_refuses_assist_and_summary_and_serves_models_and_cancel", "LocalModel.LocalModelPauseTests.Paused_refuses_start_and_stop_and_serves_status"], []),
        new("P2-052", ["Answers.AnswerEventTests.Started_then_completed_with_the_transcript_and_no_token", "Conformance.EmojiRoundTripTests.A_transcript_with_emoji_reaches_codex_and_the_desktop_unchanged"], []),
        new("P2-053", ["Answers.AnswerEventTests.A_failure_after_the_start_raises_one_failed_event_with_the_code", "Conformance.AnswerSecurityTests.Oversized_output_fails"], []),
        new("P2-054", ["Answers.AssistRouteTests.The_backend_receives_exactly_the_selection", UsualRun], []),
        new("P2-055", ["Answers.ExecutionTimingTests.Echo_and_timing_identities_hold_on_both_routes"], []),
        new("P2-056", ["Answers.AnswerSlotTests.A_second_request_is_busy_and_the_slot_frees_after_success_failure_cancel_deadline_and_abort", "Conformance.CapTests.A_storm_of_answer_requests_and_cancels_runs_one_process_at_a_time"], []),
        new("P2-057", ["Answers.RequestIdTests.Bad_ids_are_400_on_both_routes", "Conformance.InjectionTests.No_request_text_reaches_an_argument"], []),
        new("P2-058", ["Answers.CancelRouteTests.Cancel_kills_the_run_and_the_request_ends_409", "Answers.CancelRouteTests.An_unknown_id_is_404_request_not_found", "Answers.CancelRouteTests.An_encoded_slash_is_404_not_found", E2E], []),
        new("P2-059", ["Answers.AnswerDeadlineTests.A_backend_that_never_returns_is_504_at_the_deadline_counted_from_arrival"], []),
        new("P2-060", [HostileOutput], ["Answers.AnswerValidatorTests.Missing_memory_a_numeric_answer_and_non_json_fail", "Answers.AnswerValidatorTests.An_extra_member_is_dropped"]),
        new("P2-061", ["Answers.SummaryRouteTests.A_valid_summary_is_returned_with_execution", E2E], []),
        new("P2-062", ["Answers.ClientAbortTests.Closing_the_connection_kills_the_run_and_frees_the_slot_within_a_second", "Conformance.CapTests.The_gauge_never_leaks"], []),
        new("P2-063", ["Answers.CheckOrderTests.The_four_orderings_hold"], []),
        new("P2-064", [UsualRun, "Conformance.InjectionTests.The_prompt_keeps_request_text_inside_the_json_block", "Conformance.InjectionTests.The_real_child_sees_none_of_the_parents_secrets"], ["Answers.CodexAnswerBackendTests.The_argument_list_is_exact", "Answers.CodexAnswerBackendTests.The_prompt_is_exact_around_the_compact_body", "Processes.ChildEnvironmentTests.Codex_children_get_exactly_the_allowlist"], "Through the listener the argument list, the prompt, the folder and the environment names are read from the real child process."),
        new("P2-065", [E2E, HostileOutput], ["Answers.AnswerValidatorTests.The_marker_is_always_written_as_null"]),

        // Protocol 15.5: PC speech model control.
        new("P2-080", [Sequence], ["LocalModel.LocalModelControllerTests.A_new_controller_is_stopped_with_all_five_members"]),
        new("P2-081", [Sequence], ["LocalModel.LocalModelControllerTests.Start_answers_starting_and_status_then_does_not_probe"]),
        new("P2-082", [Sequence], ["LocalModel.LocalModelControllerTests.A_second_command_during_an_operation_is_refused"]),
        new("P2-083", [Sequence, "Conformance.LoadSweepTests.Route_4_reports_starting_while_the_second_stream_is_refused"], ["LocalModel.LocalModelControllerTests.After_the_gate_opens_the_model_is_loaded_and_prepare_does_not_start_again"]),
        new("P2-084", [Sequence], ["LocalModel.LocalModelControllerTests.Starting_the_other_model_switches"]),
        new("P2-085", [Sequence], ["LocalModel.LocalModelControllerTests.Stop_ends_stopped_with_one_stop_call"]),
        new("P2-086", ["Conformance.LocalModelStatementTests.P2_086_a_failed_start_is_an_error_without_private_text_and_the_latch_expires"], ["LocalModel.LocalModelControllerTests.A_failed_start_is_an_error_without_private_text"]),
        new("P2-087", ["LocalModel.LocalModelRouteTests.A_bad_model_name_is_400_and_starts_nothing"], []),
        new("P2-088", ["LocalModel.LocalModelRouteTests.Start_is_202_and_status_becomes_ready", E2E], []),
        new("P2-089", ["LocalModel.LocalModelRouteTests.A_busy_lab_refuses_stop_and_start"], []),
        new("P2-090", ["LocalModel.LocalModelRouteTests.Stopping_another_model_is_a_mismatch"], []),
        new("P2-091", [LeaseSweep, "LocalModel.LocalModelRouteTests.A_held_lease_refuses_stop_and_reports_busy"], []),
        new("P2-092", [LeaseSweep, "LocalModel.LocalModelRouteTests.After_the_lease_is_released_stop_is_202_then_stopped"], []),
        new("P2-093", ["LocalModel.LocalModelRouteTests.Starting_a_loaded_model_is_200_without_a_script_run"], []),
        new("P2-094", ["LocalModel.LocalModelRouteTests.Without_a_lab_folder_status_is_msg2_and_commands_are_503"], []),
        new("P2-095", ["Conformance.LocalModelStatementTests.P2_095_the_production_runtime_asks_for_the_relays_own_script_copies"], ["LocalModel.LabRuntimeTests.The_two_scripts_run_with_the_exact_arguments_and_the_allowlist", "LocalModel.LabRuntimeTests.The_script_that_runs_is_the_relays_own_copy"], "The production runtime behind the listener; the process runner records the request for PowerShell and starts nothing."),

        // Protocol 15.6: speech stream.
        new("P2-100", ["Speech.SpeechHappyPathTests.Each_model_is_accepted_loads_and_becomes_ready", E2E], []),
        new("P2-101", ["Speech.SpeechHappyPathTests.Audio_reaches_the_lab_byte_for_byte_and_prepare_got_the_model", E2E], []),
        new("P2-102", ["Speech.SpeechHappyPathTests.The_stream_completes_and_closes_with_1000", E2E], []),
        new("P2-103", [PrivateSweep, "Conformance.EmojiRoundTripTests.Speech_frames_with_emoji_reach_the_phone_unchanged_and_stay_within_C21"], ["Speech.LabEventFilterTests.Unknown_members_and_types_are_dropped", "Speech.LabEventFilterTests.A_long_transcript_is_cut_without_splitting_a_pair"]),
        new("P2-104", ["Speech.SpeechAdmissionTests.An_unknown_or_missing_model_is_a_typed_refusal_and_nothing_is_called"], []),
        new("P2-105", ["Speech.SpeechPrepareTests.A_failed_prepare_is_model_load_failed_without_private_text", PrivateSweep], []),
        new("P2-106", ["Conformance.LoadSweepTests.Route_4_reports_starting_while_the_second_stream_is_refused", "Speech.SpeechPrepareTests.A_load_in_progress_refuses_the_next_stream_and_a_later_one_is_admitted"], []),
        new("P2-107", ["Speech.SpeechUpgradeTests.No_token_a_wrong_token_and_a_reset_token_are_http_401", ResetSweep], []),
        new("P2-108", ["Speech.SpeechUpgradeTests.An_origin_header_is_http_401"], []),
        new("P2-109", ["Speech.SpeechAdmissionTests.Paused_is_a_typed_refusal", PauseSweep], []),
        new("P2-110", ["Speech.SpeechAdmissionTests.A_second_stream_is_speech_busy_and_the_first_continues"], []),
        new("P2-111", ["Speech.SpeechSequenceTests.A_frame_before_ready_or_after_end_stream_is_out_of_sequence"], []),
        new("P2-112", ["Speech.SpeechFrameRuleTests.Odd_oversized_empty_and_unknown_text"], []),
        new("P2-113", ["Speech.SpeechLimiterTests.A_burst_beyond_the_allowance_fails_and_real_time_audio_never_does"], ["Speech.AudioLimiterTests.The_allowance_formula_holds"]),
        new("P2-114", ["Speech.SpeechWatchdogTests.Load_limit", "Speech.SpeechWatchdogTests.Idle_limit_and_not_after_end_stream", "Speech.SpeechWatchdogTests.Finalize_limit", "Speech.SpeechWatchdogTests.Session_limit"], []),
        new("P2-115", ["Speech.SpeechControlTests.Pause_ends_the_stream", "Speech.SpeechControlTests.A_reset_ends_the_stream", "Speech.SpeechControlTests.Stopping_ends_the_stream_with_1001", "Conformance.PauseSweepTests.Pausing_ends_an_open_stream_and_leaves_the_relay_usable", ResetSweep, StopSweep], []),
        new("P2-116", ["Speech.SpeechLoadingTests.Loading_frames_repeat_with_non_decreasing_seconds"], []),
        new("P2-117", ["Speech.SpeechLabFailureTests.Another_model_or_a_busy_error_first_is_model_not_ready", "Speech.SpeechLabFailureTests.A_lab_error_event", "Speech.SpeechLabFailureTests.A_non_object_message_or_an_early_stream_complete", "Speech.SpeechLabFailureTests.The_lab_closing_early", "Speech.SpeechLabFailureTests.An_unreachable_lab"], []),
        new("P2-118", ["Speech.SpeechCloseTests.Every_abnormal_close_has_exactly_one_error_frame_and_its_code_as_the_reason", PrivateSweep], []),
        new("P2-119", ["Speech.SpeechUpgradeTests.One_socket_beyond_the_cap_is_http_429", "Conformance.CapTests.Refused_sockets_count_until_closed"], []),
        new("P2-120", ["Speech.SpeechEventTests.Loading_ready_and_end_events_and_one_text_event_per_final_segment_without_the_token", TokenSweep], []),
        new("P2-121", ["Speech.SpeechLeaseTests.A_new_stream_is_admitted_after_a_clean_end_an_error_end_and_a_phone_abort"], []),

        // Protocol 15.7: token and pairing storage.
        new("P2-140", [Stored], ["Pairing.PairingTokenTests.The_hash_verifies_the_token_and_does_not_contain_it"]),
        new("P2-141", [Stored], ["Pairing.PairingStoreTests.Create_writes_a_hash_and_a_protected_copy_and_never_the_token", "Pairing.DpapiPairingTests.Create_then_load_with_the_real_protector"]),
        new("P2-142", [Stored], ["Pairing.PairingStoreTests.Reload_returns_the_same_token_and_showing_twice_leaves_the_file_identical", "Pairing.PairingPayloadTests.The_payload_is_the_exact_text_of_the_protocol"]),
        new("P2-143", ["Pairing.PairingResetTests.The_old_token_is_401_and_the_new_one_200_without_a_restart", ResetSweep, "Conformance.ResetSweepTests.A_second_relay_on_the_same_folder_accepts_only_the_token_of_the_last_reset"], []),
        new("P2-144", [Stored], ["Pairing.QrMatrixTests.The_matrix_is_square_at_least_21_and_is_the_byte_mode_level_M_encoding_of_the_payload", "Pairing.PairingQrTests.The_record_has_no_token_member"], "The matrix is handed to the desktop, never to a route: no request reaches it. Proved on the running relay with real files and DPAPI; the encoder's independence is the area test."),
        new("P2-145", [TokenSweep], []),
        new("P2-146", ["Conformance.PairingStatementTests.P2_146_an_unreadable_pairing_file_refuses_the_start_and_is_left_untouched"], ["Pairing.PairingStoreTests.An_unreadable_file_refuses_to_start_and_is_left_untouched"]),
        new("P2-147", ["Conformance.PairingStatementTests.P2_147_the_self_check_request_is_verified_and_is_not_a_phone", "Pairing.SelfCheckTests.One_request_with_the_three_headers", "Pairing.SelfCheckTests.Refused_redirect_401_and_timeout_are_not_verified"], [], "The production self-check client is proved against a loopback stub in the area test; here the request it is asked to make is made against the relay's own listener."),
        new("P2-148", ["Conformance.PairingStatementTests.P2_148_a_bad_advertised_endpoint_is_a_start_fault", "Conformance.PairingStatementTests.P2_148_an_endpoint_without_its_slash_is_advertised_in_canonical_form"], ["Pairing.AdvertisedEndpointTests.Grammar_cases"]),
        new("P2-149", ["Answers.AnswerRevocationTests.An_assist_in_flight_at_a_reset_is_401_and_its_run_is_killed", ResetSweep], []),

        // Test plan 4.1: edge rules.
        new("S-01", ["Edge.UnauthorizedTests.No_authorization_is_the_fixed_401_on_any_method_and_target"], []),
        new("S-02", ["Edge.UnauthorizedTests.Malformed_authorization_is_the_same_401"], []),
        new("S-03", ["Conformance.EdgeSecurityTests.A_preflight_is_401_without_cors_headers", "Conformance.EdgeSecurityTests.A_browser_header_alone_with_any_value_is_the_fixed_401_on_every_route"], []),
        new("S-04", ["Conformance.EdgeSecurityTests.A_funnel_marked_request_follows_the_mode_on_every_route"], []),
        new("S-05", ["Conformance.EdgeSecurityTests.The_three_401_answers_are_identical", "Conformance.EdgeSecurityTests.An_unauthenticated_caller_sees_the_same_bytes_whatever_the_relay_is_doing"], []),
        new("S-06", ["Conformance.EdgeSecurityTests.A_401_touches_nothing"], [], "The four medians are written to the test output and are not gated, as the plan says."),
        new("S-07", ["Conformance.EdgeSecurityTests.Odd_targets_and_methods_are_404"], []),
        new("S-08", ["Conformance.EdgeSecurityTests.No_control_route_exists"], []),
        new("S-09", ["Conformance.EdgeSecurityTests.A_repeated_protocol_header_is_a_mismatch"], []),
        new("S-10", ["Conformance.EdgeSecurityTests.The_self_check_header_is_no_credential_and_a_wrong_nonce_is_a_phone"], []),

        // Test plan 4.2: malformed and oversized input.
        new("S-11", ["Conformance.ParserLimitTests.Oversized_request_line_and_headers"], []),
        new("S-12", ["Conformance.ParserLimitTests.Garbage_is_rejected_by_the_server_core"], []),
        new("S-13", ["Conformance.ParserLimitTests.Bad_framing_is_400", "Conformance.ParserLimitTests.A_broken_chunked_body_is_never_a_second_request", "Conformance.ParserLimitTests.A_content_length_the_server_core_reads_leniently_is_framed_by_its_number_and_nothing_else"], [], "Finding K-13: the server core accepts a signed Content-Length (+2, -0) and hands relay code the number only. Framing stays consistent; see the notes."),
        new("S-14", ["Conformance.BodySecurityTests.A_lying_length_cannot_exceed_the_cap"], []),
        new("S-15", ["Conformance.BodySecurityTests.Hostile_json_cases", "Conformance.BodySecurityTests.A_hundred_thousand_members_do_not_fit_a_body"], [], "100,000 members cannot fit C-10 and are refused by size; the most that fit are among the cases."),
        new("S-16", ["Conformance.InjectionTests.No_request_text_reaches_an_argument", UsualRun], []),
        new("S-17", ["LocalModel.LocalModelRouteTests.A_bad_model_name_is_400_and_starts_nothing", "Speech.SpeechAdmissionTests.An_unknown_or_missing_model_is_a_typed_refusal_and_nothing_is_called", "Conformance.EdgeSecurityTests.Forwarding_headers_and_cookies_are_ignored"], []),
        new("S-18", ["Conformance.InjectionTests.The_prompt_keeps_request_text_inside_the_json_block"], []),
        new("S-19", ["Conformance.AnswerSecurityTests.Oversized_output_fails", HostileOutput, "Conformance.AnswerSecurityTests.Output_that_is_not_utf8_fails_and_is_never_an_internal_error"], []),

        // Test plan 4.3: slow clients and concurrency.
        new("S-20", ["Conformance.SlowClientTests.Slow_headers_are_cut_off"], []),
        new("S-21", ["Conformance.SlowClientTests.A_stalled_body_releases_everything"], []),
        new("S-22", ["Conformance.SlowClientTests.A_trickled_body_is_408"], []),
        new("S-23", ["Conformance.SlowClientTests.An_unread_response_does_not_hold_a_slot", "Conformance.SlowClientTests.A_caller_without_the_token_that_never_reads_holds_one_connection_and_nothing_else"], [], "OPEN DEFECT WP-W13-1. The answer slot is not held; the place at the gauge and the connection are, for as long as the server core's response rate limit allows. Two skipped tests state what the plan expects: SlowClientTests.A_caller_that_does_not_read_gives_its_place_back_by_itself and SlowClientTests.A_caller_without_the_token_that_never_reads_is_dropped_within_seconds."),
        new("S-24", ["Conformance.SlowClientTests.Idle_connections_are_closed"], []),
        new("S-25", ["Conformance.CapTests.The_connection_cap_counts_plain_and_upgraded_connections", "Edge.ConnectionGateTests.The_cap_counts_plain_and_upgraded_connections_together"], []),
        new("S-26", ["Conformance.CapTests.The_gauge_never_leaks", "Conformance.CapTests.One_request_beyond_the_gauge_is_429_whatever_route_it_names"], []),
        new("S-27", ["Conformance.CapTests.Refused_sockets_count_until_closed", "Speech.SpeechSecurityTests.Refused_sockets_that_never_answer_the_close_give_their_places_back_after_C58"], []),
        new("S-28", ["Conformance.CapTests.A_storm_of_answer_requests_and_cancels_runs_one_process_at_a_time"], []),
        new("S-29", ["Speech.SpeechSecurityTests.A_phone_that_does_not_read_is_dropped", "Speech.SpeechLimiterTests.A_burst_beyond_the_allowance_fails_and_real_time_audio_never_does"], []),
        new("S-30", ["Speech.SpeechSecurityTests.A_socket_that_sends_nothing_is_ended_by_the_idle_limit_and_leaves_nothing_behind", "Speech.SpeechTransitionTests.Each_row_of_the_table"], []),
        new("S-31", ["Conformance.SlowClientTests.A_slow_connection_flood_does_not_starve_the_relay"], [], "While the flood holds C-30 a new connection is dropped: the cap as designed. See the notes on what must stand in front of the relay."),

        // Test plan 4.4: token handling.
        new("S-32", ["Edge.BearerCheckTests.A_token_with_a_trailing_line_break_is_the_fixed_401", "Conformance.EdgeSecurityTests.The_accepted_token_with_anything_added_changed_or_taken_away_is_refused", "Conformance.EdgeSecurityTests.White_space_around_the_header_value_is_not_part_of_it", "Conformance.EdgeSecurityTests.A_401_touches_nothing"], ["Pairing.PairingTokenTests.Verify_uses_fixed_time_comparison_and_does_not_allocate", "Pairing.PairingRevocationTests.A_reset_between_verification_and_hand_over_still_cancels_the_request"]),
        new("S-33", [Stored, ResetSweep, "Conformance.DiskFootprintTests.Only_the_expected_files_exist_after_a_full_run"], ["Pairing.PairingStoreTests.No_file_in_the_data_folder_contains_the_token"]),
        new("S-34", [ResetSweep], []),
        new("S-35", [Stored], ["Pairing.PairingQrTests.The_record_has_no_token_member", "(LiveCue.Desktop.Tests: DependencyRuleTests.Desktop_never_references_pairing_export)"]),
        new("S-36", ["Pairing.SelfCheckTests.A_redirect_is_not_followed"], []),
        new("S-37", [], ["Pairing.DpapiPairingTests.A_blob_that_does_not_unprotect_is_unreadable"], "DPAPI below the listener; the same refusal through a start is P2-146 (\"a protected copy that is not ours\")."),

        // Test plan 4.5: nothing secret or internal leaves.
        new("S-38", [PrivateSweep, "Conformance.LocalModelStatementTests.P2_086_a_failed_start_is_an_error_without_private_text_and_the_latch_expires"], []),
        new("S-39", [TokenSweep, "Conformance.TokenSweepTests.The_relay_has_no_log_to_write_a_secret_to", "Conformance.ConversationTextSweepTests.Conversation_text_is_only_in_the_events_and_answers_that_are_meant_to_carry_it"], []),
        new("S-40", ["Conformance.DiagnosticTests.An_exception_message_never_reaches_a_diagnostic"], []),
        new("S-41", ["Conformance.EdgeSecurityTests.An_unhandled_exception_is_the_fixed_500", "Conformance.EndToEndRouteTests.Every_answer_of_a_full_relay_has_only_the_allowed_headers"], []),
        new("S-42", ["Conformance.EdgeSecurityTests.Forwarding_headers_and_cookies_are_ignored"], []),
        new("S-43", ["Conformance.DiskFootprintTests.Only_the_expected_files_exist_after_a_full_run", "Conformance.DiskFootprintTests.A_start_writes_the_three_launcher_script_copies_and_the_schemas_from_the_embedded_bytes"], ["Conformance.DiskFootprintTests.The_relay_has_no_code_that_writes_outside_its_data_folder"], "\"Nothing under the OS temp folder\" is shown from the assembly, not watched: the test run shares the temp folder with every other test."),
        new("S-44", ["Conformance.InjectionTests.The_real_child_sees_none_of_the_parents_secrets", "Conformance.LocalModelStatementTests.P2_095_the_production_runtime_asks_for_the_relays_own_script_copies"], ["Processes.ProcessRunnerTests.The_three_environment_policies", "Processes.ChildEnvironmentTests.Codex_children_get_exactly_the_allowlist"]),
        new("S-45", [], ["(LiveCue.Hosting.Tests: ConsoleHostTests)"], "Owned by WP-W06."),
        new("S-46", [], ["(LiveCue.Desktop: the self-test's no-token assertion)"], "Owned by WP-W11."),

        // Test plan 4.6, the two rows assigned to WP-W13.
        new("ST-20", ["Conformance.UnreadBodyTests.An_answer_before_the_body_closes_the_connection", "Edge.UnreadBodyTests.An_answer_before_the_body_closes_the_connection"], [], "RELAY_PAUSED on routes 7 and 8 and ASSISTANT_BUSY, with the real answer area."),
        new("ST-21", ["Conformance.ParserLimitTests.Both_length_headers_are_never_framed_by_content_length", "Edge.KestrelNotesTests.K9_both_length_headers_are_never_framed_by_content_length"], []),
    ];

    /// <summary>The ids of protocol section 15 whose side is R.</summary>
    public static IReadOnlyList<string> RelayStatements { get; } =
    [
        .. Range(1, 19), .. Range(20, 21), .. Range(30, 44), .. Range(50, 65), .. Range(80, 95), .. Range(100, 121), .. Range(140, 149),
    ];

    /// <summary>The test method a name of the table stands for, or null when there is none.</summary>
    public static MethodInfo? Resolve(string name)
    {
        var parts = name.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var type = typeof(CoverageTable).Assembly.GetType($"LiveCue.Relay.Tests.{parts[0]}.{parts[1]}");
        return type?.GetMethod(parts[2], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
    }

    /// <summary>The table as the Markdown document that is kept beside this file.</summary>
    public static string Render()
    {
        var text = new StringBuilder();
        text.Append("# Relay conformance and security coverage (WP-W13)\n\n");
        text.Append("Generated from `CoverageTable.cs`; `CoverageTableTests` fails when this file and the table differ, when a\n");
        text.Append("test named here does not exist, or when a relay-side statement of protocol section 15 has no test\n");
        text.Append("through a real loopback listener. \"Result\" is the result of the Windows workflow run that carries this\n");
        text.Append("file: every test named here is part of it, so a green run is a pass for every row.\n\n");
        text.Append("A test is written `Folder.Class.Method` inside `LiveCue.Relay.Tests`.\n\n");
        Section(text, "## Protocol section 15, side R", Rows.Where(row => row.IsStatement));
        Section(text, "## Test plan section 4, the security list", Rows.Where(row => !row.IsStatement));
        text.Append(Notes.ReplaceLineEndings("\n"));
        return text.ToString();
    }

    private static void Section(StringBuilder text, string title, IEnumerable<CoverageRow> rows)
    {
        text.Append(title).Append("\n\n");
        text.Append("| Id | Through a real loopback listener | Below the listener | Result | Note |\n|---|---|---|---|---|\n");
        foreach (var row in rows)
        {
            var result = row.Note.StartsWith("OPEN DEFECT", StringComparison.Ordinal)
                ? "open defect"
                : row.Through.Length > 0 ? "pass" : "pass (not a relay request)";
            text.Append("| ").Append(row.Id)
                .Append(" | ").Append(Cell(row.Through))
                .Append(" | ").Append(Cell(row.Below))
                .Append(" | ").Append(result)
                .Append(" | ").Append(row.Note)
                .Append(" |\n");
        }

        text.Append('\n');
    }

    private static string Cell(string[] tests) =>
        tests.Length == 0 ? string.Empty : string.Join("<br>", tests.Select(test => test.StartsWith('(') ? test : "`" + test + "`"));

    private static IEnumerable<string> Range(int first, int last) =>
        Enumerable.Range(first, last - first + 1).Select(number => "P2-" + number.ToString("000", System.Globalization.CultureInfo.InvariantCulture));

    private const string Notes = """
        ## Notes

        ### The harness

        `ConformanceRelay` is one relay on a real Kestrel listener on a port the system picks, with every area the
        production one: the edge and the routes; pairing over real files in a temporary data folder, protected by
        real DPAPI; the catalogue source and the answer backend through the real process runner, with
        `LiveCue.TestChild` standing in for the Codex executable (nothing else is ever started); the PC model
        controller over the fake lab runtime (the production runtime in P2-095, with its PowerShell request
        recorded and not started); the speech bridge over the in-process fake lab socket. Only the self-check
        client is always the fake: the real one would dial the advertised address.

        ### Defects found

        | Id | What | Who can use it | State |
        |---|---|---|---|
        | WP-W13-1 | A response the caller does not read is a write that waits, and nothing bounds the wait but the server core's response rate limit (default 240 bytes a second: minutes for the 64 KB of small answers a connection can queue, more than an hour for one large answer). With the token, the request keeps its place at the gauge (C-31): sixteen such callers make every other request `429`. Without the token, a caller that sends requests and reads no `401` keeps its connection (C-30) for minutes instead of the seconds of C-40 and C-42 | With the token: any holder, when answers are large (beyond what the system buffers, roughly 64 KB) or several are requested on one connection. Without it: only a caller that reaches the loopback port itself; a proxy that reads the responses to its own requests never lets a write wait | **Open.** Two skipped tests in `SlowClientTests` state the expected behaviour and fail when run. Suggested: a limit for writing a response (the 10 s of W-04, which the speech socket already has), after which the connection is aborted; or a `MinResponseDataRate` chosen for it. It adds a number the protocol does not have, so it is the orchestrator's to rule on |
        | WP-W13-2 | A chunk size that overflows (`ffffffffffffffffffff`) was answered `500 INTERNAL_ERROR` with a diagnostic per request: the server core reports it as a plain I/O failure, which `BodyReader` let through | A caller with the token | **Fixed** in `Edge/BodyReader.cs`: `400 INVALID_REQUEST`, connection closed. Test: `ParserLimitTests.A_broken_chunked_body_is_never_a_second_request` |
        | WP-W13-3 | `LabSocketConnectorTests.An_oversized_message_is_invalid` could wait for ever (it hung once on CI) | Nobody: a defect of the test | **Fixed.** The test awaited each send before it read. After a message beyond C-23 the connector leaves the rest unread until its next read, and Windows holds a following send back while more than the send buffer's size is unread. Every exchange now starts the read first and is bounded; a second test forces the condition |
        | WP-W13-4 | `SpeechLimiterTests.Tiny_frames_are_counted_by_their_bytes_like_any_other` failed once in three full runs on the build PC | Nobody: a defect of the test | **Fixed.** It counted at the lab after the stream had ended, and a stream that ends drops what it has accepted and not yet written. It now waits until the lab has every allowed byte before it sends the one too many |

        ### Findings that are not defects of the relay

        - **K-13, a signed `Content-Length`.** The server core accepts `Content-Length: +2` and `-0` (the HTTP
          grammar allows digits only) and hands relay code the parsed number, so the relay cannot refuse it. The
          request is framed by exactly that number, consistently. A proxy that forwarded such a header unchanged
          and read it differently could be desynchronised; the Tailscale proxy parses and rewrites every request
          and refuses the header itself. Add `Content-Length: +N` to the manual framing battery of ST-27.
        - **The connection cap is the relay's only defence against a caller that holds connections.** One
          connection can be held 30 s without a byte (C-42), then 10 s with an unfinished head (C-40); sixty-four
          of them are C-30, and then nobody else connects (S-31 records it). Through the Tailscale proxy this
          cannot be done from outside: the proxy completes a request head before it opens a connection to the
          relay. It can be done by any program on the PC, which is outside what the relay defends against.
        - **The self-check reports "verified" for any `200 {"ok":true,"protocol":2}`**, as protocol 4.5 defines
          it, also when the relay never saw its own nonce arrive (the answer came from somewhere else). The nonce
          only decides "through Funnel". A stricter rule, "verified only when the nonce arrived", would make the
          text on the Pairing page true in every case. For the orchestrator, with hand-off 7.4 item 2.
        - **Request ids such as `--config` are valid** (C-03 allows the hyphen). An id is a key in memory and a
          text in an event; it is never an argument and never part of a path.

        ### Emoji and the frozen JSON writer (W-design 4.11)

        Ruled on in `EmojiRoundTripTests`: the writer's escaped surrogate pairs are a deviation from protocol
        section 1 in form only. Transcripts, answers and speech frames with characters outside the basic plane
        arrive unchanged in both directions, and no limit is passed: C-10 is checked on the wire before the
        rewrite; a speech frame is cut on a pair boundary until it fits C-21 (a transcript of emoji alone ends at
        about 10,900 emoji instead of the 16,000 of C-22); an answer is at most three times C-24 on the wire.
        What remains is the wording of protocol section 1.

        ### What is not proved here, and why

        - **Behind the real proxy.** Every test talks to the loopback listener directly. That requests arrive
          marked through Funnel and unmarked from the tailnet, that the proxy strips a mark a tailnet client
          sends, and how the proxy frames what it forwards, are ST-27 and ST-28: manual, by the owner.
        - **The real Codex.** No test starts Codex. That the containment arguments hold against a model that is
          told to read files is the live check L-4 (ST-02), which has not been run by any package.
        - **The real lab scripts and Docker.** No test starts PowerShell or Docker; P2-095 proves what would be
          started. The manual GPU check is protocol 15.10.
        - **Timing.** A test can show that a wrong token of the right shape and a near miss take the same path
          (one hash, one fixed-time comparison, no allocation) and can record their medians; it cannot prove the
          absence of a timing channel.
        - **The OS temp folder.** Shown from the assembly's references, not watched.
        - **Memory held by a hostile caller.** The bounds are structural (C-10 per body, C-20 per frame, C-73
          backlog, the queue of W-03) and each has a behavioural test; no test measures the process.

        """;
}

/// <summary>The table checked against the test assembly, the protocol's list of statements and the document.</summary>
public sealed class CoverageTableTests
{
    [Fact]
    public void Every_relay_statement_has_a_test_through_a_real_loopback_listener()
    {
        var rows = CoverageTable.Rows.Where(row => row.IsStatement).ToList();

        // Exactly the statements of side R, once each, in order.
        Assert.Equal(CoverageTable.RelayStatements, rows.Select(row => row.Id));
        Assert.Equal(100, rows.Count);

        // Each with at least one test through a listener.
        Assert.All(rows, row => Assert.True(row.Through.Length > 0, row.Id + " has no test through a listener."));
    }

    [Fact]
    public void Every_test_the_table_names_exists_and_is_not_skipped()
    {
        var missing = new List<string>();
        foreach (var name in CoverageTable.Rows.SelectMany(row => row.Through.Concat(row.Below)).Distinct().Where(name => !name.StartsWith('(')))
        {
            var method = CoverageTable.Resolve(name);
            var fact = method?.GetCustomAttribute<FactAttribute>();
            if (fact is null || fact.Skip is not null)
            {
                missing.Add(name);
            }
        }

        Assert.True(missing.Count == 0, "Not a test that runs: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_security_item_of_the_plan_has_a_row()
    {
        var ids = CoverageTable.Rows.Where(row => !row.IsStatement).Select(row => row.Id).ToList();
        var expected = Enumerable.Range(1, 46)
            .Select(number => "S-" + number.ToString("00", System.Globalization.CultureInfo.InvariantCulture))
            .Concat(["ST-20", "ST-21"]);

        Assert.Equal(expected, ids);
    }

    [Fact]
    public void The_coverage_document_is_the_table()
    {
        // The document lives in the repository beside this file. Where the tests run without a checkout
        // around them there is nothing to compare, and the three tests above still hold.
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "windows", "LiveCueRemake.sln")))
        {
            folder = folder.Parent;
        }

        if (folder is null)
        {
            return;
        }

        var path = Path.Combine(folder.FullName, "windows", "tests", "LiveCue.Relay.Tests", "Conformance", "Coverage.md");
        var expected = CoverageTable.Render();
        if (Environment.GetEnvironmentVariable("LIVECUE_WRITE_COVERAGE") == "1")
        {
            File.WriteAllText(path, expected, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        Assert.True(File.Exists(path), "Coverage.md is missing. Run this test once with LIVECUE_WRITE_COVERAGE=1.");
        Assert.Equal(expected, File.ReadAllText(path).ReplaceLineEndings("\n"));
    }
}
