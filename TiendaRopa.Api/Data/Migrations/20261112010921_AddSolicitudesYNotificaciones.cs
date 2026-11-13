using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiendaRopa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSolicitudesYNotificaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SolicitudAtencion",
                columns: table => new
                {
                    IdSolicitud = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdCliente = table.Column<int>(type: "int", nullable: false),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IdPedido = table.Column<int>(type: "int", nullable: true),
                    IdProducto = table.Column<int>(type: "int", nullable: true),
                    IdProductoVariante = table.Column<int>(type: "int", nullable: true),
                    TalleDescripcion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ColorDescripcion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Mensaje = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TelefonoContacto = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TelefonoEstado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Respuesta = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IdUsuarioResuelve = table.Column<int>(type: "int", nullable: true),
                    FechaResolucion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaveIdempotencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IdPropuesta = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudAtencion", x => x.IdSolicitud);
                    table.ForeignKey(
                        name: "FK_SolicitudAtencion_Cliente_IdCliente",
                        column: x => x.IdCliente,
                        principalTable: "Cliente",
                        principalColumn: "IdCliente",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SolicitudAtencion_Pedido_IdPedido",
                        column: x => x.IdPedido,
                        principalTable: "Pedido",
                        principalColumn: "IdPedido",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SolicitudAtencion_Producto_IdProducto",
                        column: x => x.IdProducto,
                        principalTable: "Producto",
                        principalColumn: "IdProducto",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SolicitudPropuesta",
                columns: table => new
                {
                    IdPropuesta = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    IdCliente = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IdPedido = table.Column<int>(type: "int", nullable: true),
                    IdProducto = table.Column<int>(type: "int", nullable: true),
                    IdProductoVariante = table.Column<int>(type: "int", nullable: true),
                    TalleDescripcion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ColorDescripcion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Mensaje = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TelefonoContacto = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TelefonoEstado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GuardarTelefonoEnPerfil = table.Column<bool>(type: "bit", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaConfirmacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdSolicitudGenerada = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudPropuesta", x => x.IdPropuesta);
                });

            migrationBuilder.CreateTable(
                name: "Notificacion",
                columns: table => new
                {
                    IdNotificacion = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    IdSolicitud = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Mensaje = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Leida = table.Column<bool>(type: "bit", nullable: false),
                    FechaLectura = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notificacion", x => x.IdNotificacion);
                    table.ForeignKey(
                        name: "FK_Notificacion_SolicitudAtencion_IdSolicitud",
                        column: x => x.IdSolicitud,
                        principalTable: "SolicitudAtencion",
                        principalColumn: "IdSolicitud",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notificacion_IdSolicitud",
                table: "Notificacion",
                column: "IdSolicitud");

            migrationBuilder.CreateIndex(
                name: "IX_Notificacion_IdUsuario_Leida",
                table: "Notificacion",
                columns: new[] { "IdUsuario", "Leida" });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudAtencion_ClaveIdempotencia",
                table: "SolicitudAtencion",
                column: "ClaveIdempotencia",
                unique: true,
                filter: "[ClaveIdempotencia] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudAtencion_Estado",
                table: "SolicitudAtencion",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudAtencion_IdCliente",
                table: "SolicitudAtencion",
                column: "IdCliente");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudAtencion_IdPedido",
                table: "SolicitudAtencion",
                column: "IdPedido");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudAtencion_IdProducto",
                table: "SolicitudAtencion",
                column: "IdProducto");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudAtencion_IdPropuesta",
                table: "SolicitudAtencion",
                column: "IdPropuesta",
                unique: true,
                filter: "[IdPropuesta] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudPropuesta_IdUsuario_Estado",
                table: "SolicitudPropuesta",
                columns: new[] { "IdUsuario", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notificacion");

            migrationBuilder.DropTable(
                name: "SolicitudPropuesta");

            migrationBuilder.DropTable(
                name: "SolicitudAtencion");
        }
    }
}
