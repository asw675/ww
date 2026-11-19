namespace TideCombat
{
    // Labels used by the non-combat task ports. Keep in sync with Signatures.tsv;
    // tools/BuildSignatures.cs regenerates them.
    internal static class TaskIndicators
    {
        internal static readonly string[] All =
        {
            "a_garden_back", "a_garden_restart", "garden_confirm", "garden_continue_game",
            "garden_get_confirm_gray", "garden_get_gold", "garden_get_purple", "garden_get_skip",
            "garden_new_stage", "garden_next_day", "garden_not_interested",
            "garden_not_interested_confirm", "garden_shop_buy", "garden_shop_close",
            "garden_start_game", "garden_tip_close", "garden_unpause", "gray_book_quest",
            "map_way_point", "gray_confirm_exit_button", "gem_add_stamina", "fast_travel_custom",
            "remove_custom", "team_close", "echo_dropped", "echo_not_dropped", "echo_locked",
            "echo_not_locked", "echo_enhance_btn", "echo_select_all"
        };
    }
}
