using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_activation_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code_hash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_activation_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_run_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    response = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_run_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    system_prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    disabled_tools_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    prompt_history_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_provider_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    api_key_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    base_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_provider_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_usage_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    input_tokens = table.Column<int>(type: "int", nullable: false),
                    output_tokens = table.Column<int>(type: "int", nullable: false),
                    total_tokens = table.Column<int>(type: "int", nullable: false),
                    estimated_cost_usd = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    success = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_usage_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "automation_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    trigger = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    threshold_minutes = table.Column<int>(type: "int", nullable: false),
                    stage_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    time_window_start = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    time_window_end = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    follow_up_title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    template_category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    shift_name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    execution_count = table.Column<int>(type: "int", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    modal_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    contact_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    whats_app_line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_message_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    friendly_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    smtp_host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    smtp_port = table.Column<int>(type: "int", nullable: false),
                    smtp_user = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    smtp_password_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    use_ssl = table.Column<bool>(type: "bit", nullable: false),
                    from_email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    from_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "evolution_master_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    base_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    api_key_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    webhook_mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Development"),
                    webhook_public_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    webhook_active_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    webhook_token = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    meta_webhook_verify_token = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evolution_master_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "google_auth_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_id = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    client_secret_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_google_auth_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    media_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    media_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    media_mime_type = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_closed_won = table.Column<bool>(type: "bit", nullable: false),
                    is_closed_lost = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pipeline_stages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_brandings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    tagline = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    login_logo_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    login_headline = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    login_subtext = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_brandings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    email_verified = table.Column<bool>(type: "bit", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    avatar_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    google_subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    auth_provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    platform_role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quote_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    html_content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    send_as_image = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saas_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    monthly_price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    yearly_price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saas_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "super_admin_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    action_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    entity_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    previous_value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_super_admin_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "task_boards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_boards", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    mime_type = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_api_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    api_key_hash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    api_key_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_api_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_blocked_numbers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_blocked_numbers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    config_key = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    config_value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_configurations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_evolution_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    use_master_server = table.Column<bool>(type: "bit", nullable: false),
                    base_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    instance_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    api_token_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    webhook_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_evolution_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    legal_name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    tax_id = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    country = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    logo_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    online_booking_enabled = table.Column<bool>(type: "bit", nullable: false),
                    public_booking_token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    public_booking_base_url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "whats_app_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instance_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    phone_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    assigned_to_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_connected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_status_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    cloud_phone_number_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    cloud_business_account_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    cloud_access_token_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whats_app_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wompi_master_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    environment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    public_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    private_key_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    events_secret_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    integrity_secret_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    webhook_endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    max_retries = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wompi_master_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wompi_webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_event_id = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    signature_valid = table.Column<bool>(type: "bit", nullable: false),
                    raw_payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    processing_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    transaction_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wompi_webhook_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_cache_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    field_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_updatable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_cache_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agent_cache_fields_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_cache_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    field_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_cache_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agent_cache_values_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_prompts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    rule = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_prompts", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agent_prompts_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    resource_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    file_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_resources", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agent_resources_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    direction = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    external_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    message_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    sent_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sent_by_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    media_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    media_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    media_mime_type = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    reaction = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    contact_phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    destination = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    estimated_value = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    stage_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_to_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    business_unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    loss_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    stage_changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    field_values_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    archive_reason = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    archive_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    archived_by_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                    table.ForeignKey(
                        name: "fk_leads_pipeline_stages_stage_id",
                        column: x => x.stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stage_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    field_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    field_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    column = table.Column<int>(type: "int", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    options = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    allow_multiple = table.Column<bool>(type: "bit", nullable: false),
                    show_in_filter = table.Column<bool>(type: "bit", nullable: false),
                    multi_with_detail = table.Column<bool>(type: "bit", nullable: false),
                    total_source_keys = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    repeat_with_field_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pipeline_field_definitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_pipeline_field_definitions_pipeline_stages_stage_id",
                        column: x => x.stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    tenant_role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    lead_visibility = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    invitation_token = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    invitation_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_users_platform_users_platform_user_id",
                        column: x => x.platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saas_plan_limits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    limit_key = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    limit_value = table.Column<long>(type: "bigint", nullable: false),
                    limit_unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    enforcement_mode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saas_plan_limits", x => x.id);
                    table.ForeignKey(
                        name: "fk_saas_plan_limits_saas_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "saas_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_board_columns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    board_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_done = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_board_columns", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_board_columns_task_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "task_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    board_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_tags_task_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "task_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    billing_frequency = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    current_period_ends_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    grace_period_ends_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    auto_renew = table.Column<bool>(type: "bit", nullable: false),
                    wompi_payment_source_id = table.Column<long>(type: "bigint", nullable: true),
                    payment_method_label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    failed_attempts = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_subscriptions_saas_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "saas_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_subscriptions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_line_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    whats_app_line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_connected = table.Column<bool>(type: "bit", nullable: false),
                    auto_confirm = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_line_bindings", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agent_line_bindings_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_agent_line_bindings_whats_app_lines_whats_app_line_id",
                        column: x => x.whats_app_line_id,
                        principalTable: "whats_app_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "follow_up_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    assigned_to_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_up_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_follow_up_tasks_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lead_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    activity_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_lead_activities_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lead_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_lead_files_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lead_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_lead_notes_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_cards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    board_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    column_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    color = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_cards_task_board_columns_column_id",
                        column: x => x.column_id,
                        principalTable: "task_board_columns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_cards_task_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "task_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    provider_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    billing_period_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    billing_period_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_payments_tenant_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "tenant_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_card_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_activities_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_assignments_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_card_assignments_tenant_users_tenant_user_id",
                        column: x => x.tenant_user_id,
                        principalTable: "tenant_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    mime_type = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    uploaded_by_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_attachments_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_checklist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_completed = table.Column<bool>(type: "bit", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_checklist_items_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_tag_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tag_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_tag_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_tag_assignments_task_card_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "task_card_tags",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_task_card_tag_assignments_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_activation_codes_platform_user_id",
                table: "account_activation_codes",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_cache_fields_agent_id_field_key",
                table: "ai_agent_cache_fields",
                columns: new[] { "agent_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_cache_fields_tenant_id_agent_id_sort_order",
                table: "ai_agent_cache_fields",
                columns: new[] { "tenant_id", "agent_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_cache_values_agent_id_session_id_field_key",
                table: "ai_agent_cache_values",
                columns: new[] { "agent_id", "session_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_cache_values_tenant_id_agent_id_session_id",
                table: "ai_agent_cache_values",
                columns: new[] { "tenant_id", "agent_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_agent_id",
                table: "ai_agent_line_bindings",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_tenant_id_agent_id",
                table: "ai_agent_line_bindings",
                columns: new[] { "tenant_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_tenant_id_whats_app_line_id",
                table: "ai_agent_line_bindings",
                columns: new[] { "tenant_id", "whats_app_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_whats_app_line_id",
                table: "ai_agent_line_bindings",
                column: "whats_app_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_prompts_agent_id",
                table: "ai_agent_prompts",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_prompts_tenant_id_agent_id_sort_order",
                table: "ai_agent_prompts",
                columns: new[] { "tenant_id", "agent_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_resources_agent_id",
                table: "ai_agent_resources",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_resources_tenant_id_agent_id_sort_order",
                table: "ai_agent_resources",
                columns: new[] { "tenant_id", "agent_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_run_logs_tenant_id_conversation_id_occurred_at",
                table: "ai_agent_run_logs",
                columns: new[] { "tenant_id", "conversation_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agents_tenant_id_sort_order",
                table: "ai_agents",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_configs_provider",
                table: "ai_provider_configs",
                column: "provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_logs_tenant_id_agent_id",
                table: "ai_usage_logs",
                columns: new[] { "tenant_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_logs_tenant_id_created_at",
                table: "ai_usage_logs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_automation_rules_tenant_id_sort_order",
                table: "automation_rules",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_business_units_tenant_id_sort_order",
                table: "business_units",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_whats_app_line_id_contact_phone",
                table: "conversations",
                columns: new[] { "tenant_id", "whats_app_line_id", "contact_phone" },
                unique: true,
                filter: "[whats_app_line_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_lead_id",
                table: "follow_up_tasks",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_tenant_id_due_at",
                table: "follow_up_tasks",
                columns: new[] { "tenant_id", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_tenant_id_status",
                table: "follow_up_tasks",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_lead_activities_lead_id",
                table: "lead_activities",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_lead_activities_tenant_id_lead_id",
                table: "lead_activities",
                columns: new[] { "tenant_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lead_files_lead_id",
                table: "lead_files",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_lead_files_tenant_id_lead_id",
                table: "lead_files",
                columns: new[] { "tenant_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lead_notes_lead_id",
                table: "lead_notes",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_lead_notes_tenant_id_lead_id",
                table: "lead_notes",
                columns: new[] { "tenant_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_assigned_to_tenant_user_id",
                table: "leads",
                column: "assigned_to_tenant_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_stage_id",
                table: "leads",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_id_archived_at",
                table: "leads",
                columns: new[] { "tenant_id", "archived_at" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_id_stage_id",
                table: "leads",
                columns: new[] { "tenant_id", "stage_id" });

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_tenant_id_category_sort_order",
                table: "message_templates",
                columns: new[] { "tenant_id", "category", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id",
                table: "messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_conversation_id",
                table: "messages",
                columns: new[] { "tenant_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_external_id",
                table: "messages",
                columns: new[] { "tenant_id", "external_id" },
                unique: true,
                filter: "[external_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_platform_user_id",
                table: "password_reset_tokens",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_field_definitions_stage_id_field_key",
                table: "pipeline_field_definitions",
                columns: new[] { "stage_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_field_definitions_tenant_id_stage_id_sort_order",
                table: "pipeline_field_definitions",
                columns: new[] { "tenant_id", "stage_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_tenant_id_name",
                table: "pipeline_stages",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_tenant_id_sort_order",
                table: "pipeline_stages",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_email",
                table: "platform_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_google_subject",
                table: "platform_users",
                column: "google_subject",
                unique: true,
                filter: "[google_subject] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_quote_templates_tenant_id_is_default",
                table: "quote_templates",
                columns: new[] { "tenant_id", "is_default" });

            migrationBuilder.CreateIndex(
                name: "ix_saas_plan_limits_plan_id_limit_key",
                table: "saas_plan_limits",
                columns: new[] { "plan_id", "limit_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_super_admin_audit_logs_created_at",
                table: "super_admin_audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_super_admin_audit_logs_tenant_id",
                table: "super_admin_audit_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_board_columns_board_id",
                table: "task_board_columns",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_board_columns_tenant_id_board_id_sort_order",
                table: "task_board_columns",
                columns: new[] { "tenant_id", "board_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_boards_tenant_id_sort_order",
                table: "task_boards",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_card_activities_task_card_id_created_at",
                table: "task_card_activities",
                columns: new[] { "task_card_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_card_assignments_task_card_id_tenant_user_id",
                table: "task_card_assignments",
                columns: new[] { "task_card_id", "tenant_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_card_assignments_tenant_user_id",
                table: "task_card_assignments",
                column: "tenant_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_card_attachments_task_card_id_created_at",
                table: "task_card_attachments",
                columns: new[] { "task_card_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_card_checklist_items_task_card_id_sort_order",
                table: "task_card_checklist_items",
                columns: new[] { "task_card_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_card_tag_assignments_tag_id",
                table: "task_card_tag_assignments",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_card_tag_assignments_task_card_id_tag_id",
                table: "task_card_tag_assignments",
                columns: new[] { "task_card_id", "tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_card_tags_board_id_name",
                table: "task_card_tags",
                columns: new[] { "board_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_cards_board_id",
                table: "task_cards",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_cards_column_id",
                table: "task_cards",
                column: "column_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_cards_tenant_id_board_id_column_id_sort_order",
                table: "task_cards",
                columns: new[] { "tenant_id", "board_id", "column_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_cards_tenant_id_is_archived",
                table: "task_cards",
                columns: new[] { "tenant_id", "is_archived" });

            migrationBuilder.CreateIndex(
                name: "ix_template_assets_tenant_id_created_at",
                table: "template_assets",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_configs_api_key_hash",
                table: "tenant_api_configs",
                column: "api_key_hash");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_configs_tenant_id",
                table: "tenant_api_configs",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_blocked_numbers_tenant_id_phone",
                table: "tenant_blocked_numbers",
                columns: new[] { "tenant_id", "phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_configurations_tenant_id_config_key",
                table: "tenant_configurations",
                columns: new[] { "tenant_id", "config_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_evolution_configs_tenant_id",
                table: "tenant_evolution_configs",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_payments_subscription_id",
                table: "tenant_payments",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_payments_tenant_id",
                table: "tenant_payments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_subscriptions_plan_id",
                table: "tenant_subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_subscriptions_tenant_id",
                table: "tenant_subscriptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_users_invitation_token",
                table: "tenant_users",
                column: "invitation_token");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_users_platform_user_id",
                table: "tenant_users",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_users_tenant_id_email",
                table: "tenant_users",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_users_tenant_id_platform_user_id",
                table: "tenant_users",
                columns: new[] { "tenant_id", "platform_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_public_booking_token",
                table: "tenants",
                column: "public_booking_token",
                unique: true,
                filter: "[public_booking_token] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_lines_assigned_to_tenant_user_id",
                table: "whats_app_lines",
                column: "assigned_to_tenant_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_lines_cloud_phone_number_id",
                table: "whats_app_lines",
                column: "cloud_phone_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_lines_tenant_id_instance_name",
                table: "whats_app_lines",
                columns: new[] { "tenant_id", "instance_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wompi_webhook_events_provider_event_id",
                table: "wompi_webhook_events",
                column: "provider_event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_activation_codes");

            migrationBuilder.DropTable(
                name: "ai_agent_cache_fields");

            migrationBuilder.DropTable(
                name: "ai_agent_cache_values");

            migrationBuilder.DropTable(
                name: "ai_agent_line_bindings");

            migrationBuilder.DropTable(
                name: "ai_agent_prompts");

            migrationBuilder.DropTable(
                name: "ai_agent_resources");

            migrationBuilder.DropTable(
                name: "ai_agent_run_logs");

            migrationBuilder.DropTable(
                name: "ai_provider_configs");

            migrationBuilder.DropTable(
                name: "ai_usage_logs");

            migrationBuilder.DropTable(
                name: "automation_rules");

            migrationBuilder.DropTable(
                name: "business_units");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "email_configs");

            migrationBuilder.DropTable(
                name: "evolution_master_configs");

            migrationBuilder.DropTable(
                name: "follow_up_tasks");

            migrationBuilder.DropTable(
                name: "google_auth_configs");

            migrationBuilder.DropTable(
                name: "lead_activities");

            migrationBuilder.DropTable(
                name: "lead_files");

            migrationBuilder.DropTable(
                name: "lead_notes");

            migrationBuilder.DropTable(
                name: "message_templates");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "pipeline_field_definitions");

            migrationBuilder.DropTable(
                name: "platform_brandings");

            migrationBuilder.DropTable(
                name: "quote_templates");

            migrationBuilder.DropTable(
                name: "saas_plan_limits");

            migrationBuilder.DropTable(
                name: "super_admin_audit_logs");

            migrationBuilder.DropTable(
                name: "task_card_activities");

            migrationBuilder.DropTable(
                name: "task_card_assignments");

            migrationBuilder.DropTable(
                name: "task_card_attachments");

            migrationBuilder.DropTable(
                name: "task_card_checklist_items");

            migrationBuilder.DropTable(
                name: "task_card_tag_assignments");

            migrationBuilder.DropTable(
                name: "template_assets");

            migrationBuilder.DropTable(
                name: "tenant_api_configs");

            migrationBuilder.DropTable(
                name: "tenant_blocked_numbers");

            migrationBuilder.DropTable(
                name: "tenant_configurations");

            migrationBuilder.DropTable(
                name: "tenant_evolution_configs");

            migrationBuilder.DropTable(
                name: "tenant_payments");

            migrationBuilder.DropTable(
                name: "wompi_master_configs");

            migrationBuilder.DropTable(
                name: "wompi_webhook_events");

            migrationBuilder.DropTable(
                name: "whats_app_lines");

            migrationBuilder.DropTable(
                name: "ai_agents");

            migrationBuilder.DropTable(
                name: "leads");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "tenant_users");

            migrationBuilder.DropTable(
                name: "task_card_tags");

            migrationBuilder.DropTable(
                name: "task_cards");

            migrationBuilder.DropTable(
                name: "tenant_subscriptions");

            migrationBuilder.DropTable(
                name: "pipeline_stages");

            migrationBuilder.DropTable(
                name: "platform_users");

            migrationBuilder.DropTable(
                name: "task_board_columns");

            migrationBuilder.DropTable(
                name: "saas_plans");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "task_boards");
        }
    }
}
