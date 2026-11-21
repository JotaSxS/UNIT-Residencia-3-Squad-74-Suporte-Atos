using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupportAtos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarBootstrapEAtivacaoFuncionarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ativacao_consumida_em",
                table: "usuario_credencial",
                type: "timestamp",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "token_ativacao_expira_em",
                table: "usuario_credencial",
                type: "timestamp",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token_ativacao_hash",
                table: "usuario_credencial",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "autenticacao_bootstrap",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    executado_em = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_autenticacao_bootstrap", x => x.id);
                    table.CheckConstraint("ck_bootstrap_unico", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_usuario_credencial_token_ativacao_hash",
                table: "usuario_credencial",
                column: "token_ativacao_hash",
                unique: true,
                filter: "token_ativacao_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autenticacao_bootstrap");

            migrationBuilder.DropIndex(
                name: "IX_usuario_credencial_token_ativacao_hash",
                table: "usuario_credencial");

            migrationBuilder.DropColumn(
                name: "ativacao_consumida_em",
                table: "usuario_credencial");

            migrationBuilder.DropColumn(
                name: "token_ativacao_expira_em",
                table: "usuario_credencial");

            migrationBuilder.DropColumn(
                name: "token_ativacao_hash",
                table: "usuario_credencial");
        }
    }
}
