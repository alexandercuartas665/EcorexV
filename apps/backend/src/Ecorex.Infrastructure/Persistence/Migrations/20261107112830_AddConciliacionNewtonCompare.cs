using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConciliacionNewtonCompare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "origen",
                table: "conciliacion_dian_renglones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "descuento_comercial",
                table: "conciliacion_dian_newton_dummies",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_doc",
                table: "conciliacion_dian_newton_dummies",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<decimal>(
                name: "iva",
                table: "conciliacion_dian_newton_dummies",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "nit_emisor",
                table: "conciliacion_dian_newton_dummies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "nombre_emisor",
                table: "conciliacion_dian_newton_dummies",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "numero_factura",
                table: "conciliacion_dian_newton_dummies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "retencion_fuente",
                table: "conciliacion_dian_newton_dummies",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "retencion_ica",
                table: "conciliacion_dian_newton_dummies",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "subtotal_bruto",
                table: "conciliacion_dian_newton_dummies",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "subtotal_neto",
                table: "conciliacion_dian_newton_dummies",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "tipo_doc",
                table: "conciliacion_dian_newton_dummies",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tipo_pago",
                table: "conciliacion_dian_newton_dummies",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "total",
                table: "conciliacion_dian_newton_dummies",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_antes_ret",
                table: "conciliacion_dian_newton_dummies",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "origen",
                table: "conciliacion_dian_renglones");

            migrationBuilder.DropColumn(
                name: "descuento_comercial",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "fecha_doc",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "iva",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "nit_emisor",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "nombre_emisor",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "numero_factura",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "retencion_fuente",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "retencion_ica",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "subtotal_bruto",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "subtotal_neto",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "tipo_doc",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "tipo_pago",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "total",
                table: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropColumn(
                name: "total_antes_ret",
                table: "conciliacion_dian_newton_dummies");
        }
    }
}
