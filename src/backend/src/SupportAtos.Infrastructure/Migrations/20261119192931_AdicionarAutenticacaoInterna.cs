using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SupportAtos.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Esta migration adiciona suporte à autenticação interna (login próprio com e-mail e senha)
    /// para funcionários da equipe (Admin, Supervisor, Agente).
    ///
    /// Rollback (Down):
    /// - Remove as tabelas ''sessao_autenticacao'' e ''usuario_credencial'', destruindo
    ///   todas as credenciais de senha e sessões de refresh token armazenadas.
    /// - Reverte ''usuario.grupo_empresa_id'' para NOT NULL.
    /// - Bloqueia a reversão se existirem usuários sem grupo de empresa, para evitar
    ///   órfãos sem grupo.
    /// </remarks>
    public partial class AdicionarAutenticacaoInterna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "grupo_empresa_id",
                table: "usuario",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "sessao_autenticacao",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usuario_id = table.Column<int>(type: "integer", nullable: false),
                    refresh_token_hash = table.Column<string>(type: "varchar(255)", nullable: false),
                    refresh_token_expiracao = table.Column<DateTime>(type: "timestamp", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp", nullable: false),
                    revogado_em = table.Column<DateTime>(type: "timestamp", nullable: true),
                    substituido_por_refresh_token_hash = table.Column<string>(type: "varchar(255)", nullable: true),
                    endereco_ip = table.Column<string>(type: "varchar(45)", nullable: true),
                    user_agent = table.Column<string>(type: "varchar(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessao_autenticacao", x => x.id);
                    table.ForeignKey(
                        name: "FK_sessao_autenticacao_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_credencial",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usuario_id = table.Column<int>(type: "integer", nullable: false),
                    senha_hash = table.Column<string>(type: "varchar(255)", nullable: false),
                    data_criacao = table.Column<DateTime>(type: "timestamp", nullable: false),
                    data_ultima_alteracao = table.Column<DateTime>(type: "timestamp", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    tentativas_falhas = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    bloqueado_ate = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuario_credencial", x => x.id);
                    table.ForeignKey(
                        name: "FK_usuario_credencial_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sessao_autenticacao_refresh_token_hash",
                table: "sessao_autenticacao",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessao_autenticacao_revogado_em",
                table: "sessao_autenticacao",
                column: "revogado_em");

            migrationBuilder.CreateIndex(
                name: "IX_sessao_autenticacao_usuario_id",
                table: "sessao_autenticacao",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_usuario_credencial_usuario_id",
                table: "usuario_credencial",
                column: "usuario_id",
                unique: true);

            migrationBuilder.Sql(@"ALTER TABLE ""usuario"" ADD CONSTRAINT ""chk_usuario_cliente_grupo_empresa"" CHECK (""papel"" <> 'Cliente' OR ""grupo_empresa_id"" IS NOT NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Verifica se existem usuários sem grupo antes de tornar a coluna NOT NULL.
            // Se houver registros incompatíveis, o rollback é interrompido com erro claro
            // para evitar perda de integridade (usuários órfãos sem grupo).
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM ""usuario"" WHERE ""grupo_empresa_id"" IS NULL) THEN
                        RAISE EXCEPTION 'Não é possível reverter: existem usuários sem grupo de empresa. Associe-os a um grupo antes de remover esta migration.';
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"ALTER TABLE ""usuario"" DROP CONSTRAINT IF EXISTS ""chk_usuario_cliente_grupo_empresa"";");

            migrationBuilder.DropTable(
                name: "sessao_autenticacao");

            migrationBuilder.DropTable(
                name: "usuario_credencial");

            migrationBuilder.AlterColumn<int>(
                name: "grupo_empresa_id",
                table: "usuario",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
