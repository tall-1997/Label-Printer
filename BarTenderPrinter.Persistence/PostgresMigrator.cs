using Npgsql;

namespace BarTenderPrinter.Persistence;

public sealed class PostgresMigrator(NpgsqlDataSource dataSource)
{
    private const long MigrationLockId = 6_287_424_503;

    private static readonly (int Version, string Sql)[] Migrations =
    {
        (1, """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version integer PRIMARY KEY,
            applied_at_utc timestamptz NOT NULL
        );

        CREATE TABLE production_orders (
            id text PRIMARY KEY,
            order_number text NOT NULL UNIQUE,
            customer text NOT NULL,
            product_model text NOT NULL,
            color text NOT NULL,
            planned_quantity integer NOT NULL CHECK (planned_quantity > 0),
            valid_from_utc timestamptz NULL,
            valid_to_utc timestamptz NULL,
            status text NOT NULL,
            version bigint NOT NULL CHECK (version >= 0)
        );

        CREATE TABLE number_ranges (
            id text PRIMARY KEY,
            order_id text NOT NULL REFERENCES production_orders(id),
            number_type text NOT NULL,
            prefix text NOT NULL,
            date_pattern text NOT NULL,
            start_value bigint NOT NULL,
            end_value bigint NOT NULL,
            next_value bigint NOT NULL,
            step bigint NOT NULL,
            numeric_width integer NOT NULL,
            validation_pattern text NOT NULL,
            is_exhausted boolean NOT NULL DEFAULT false,
            version bigint NOT NULL CHECK (version >= 0)
        );

        CREATE TABLE number_allocations (
            id text PRIMARY KEY,
            range_id text NOT NULL REFERENCES number_ranges(id),
            value text NOT NULL,
            unit_id text NOT NULL DEFAULT '',
            station_id text NOT NULL,
            operator_id text NOT NULL,
            status text NOT NULL,
            idempotency_key text NOT NULL UNIQUE,
            request_hash text NOT NULL,
            allocated_at_utc timestamptz NOT NULL,
            UNIQUE (range_id, value)
        );

        CREATE TABLE production_units (
            id text PRIMARY KEY,
            order_id text NOT NULL REFERENCES production_orders(id),
            status text NOT NULL,
            current_operation_id text NOT NULL,
            identifiers_json jsonb NOT NULL,
            version bigint NOT NULL CHECK (version >= 0)
        );
        CREATE UNIQUE INDEX ux_production_units_sn ON production_units ((identifiers_json->>'SerialNumber')) WHERE identifiers_json ? 'SerialNumber';
        CREATE UNIQUE INDEX ux_production_units_imei ON production_units ((identifiers_json->>'Imei')) WHERE identifiers_json ? 'Imei';

        CREATE TABLE station_pass_records (
            id text PRIMARY KEY,
            unit_id text NOT NULL REFERENCES production_units(id),
            order_id text NOT NULL REFERENCES production_orders(id),
            route_id text NOT NULL,
            operation_id text NOT NULL,
            station_id text NOT NULL,
            operator_id text NOT NULL,
            occurred_at_utc timestamptz NOT NULL,
            idempotency_key text NOT NULL UNIQUE,
            request_hash text NOT NULL,
            rework_order_id text NOT NULL,
            rework_sequence integer NOT NULL
        );

        CREATE TABLE packaging_units (
            id text PRIMARY KEY,
            order_id text NOT NULL REFERENCES production_orders(id),
            unit_type text NOT NULL,
            code text NOT NULL UNIQUE,
            product_model text NOT NULL,
            color text NOT NULL,
            capacity integer NOT NULL,
            status text NOT NULL,
            version bigint NOT NULL CHECK (version >= 0)
        );

        CREATE TABLE packaging_bindings (
            parent_id text NOT NULL REFERENCES packaging_units(id),
            child_id text NOT NULL REFERENCES packaging_units(id),
            bound_at_utc timestamptz NOT NULL,
            operator_id text NOT NULL,
            is_active boolean NOT NULL DEFAULT true,
            PRIMARY KEY (parent_id, child_id, bound_at_utc)
        );
        CREATE UNIQUE INDEX ux_packaging_bindings_active_child ON packaging_bindings(child_id) WHERE is_active;

        CREATE TABLE print_jobs (
            job_id text PRIMARY KEY,
            idempotency_key text NOT NULL UNIQUE,
            label_type text NOT NULL,
            template_id text NOT NULL,
            template_version text NOT NULL,
            state text NOT NULL,
            request_hash text NOT NULL,
            request_json jsonb NOT NULL,
            result_json jsonb NULL,
            version bigint NOT NULL DEFAULT 0,
            created_at_utc timestamptz NOT NULL,
            updated_at_utc timestamptz NOT NULL
        );

        CREATE TABLE audit_events (
            id text PRIMARY KEY,
            actor_id text NOT NULL,
            station_id text NOT NULL,
            shift_id text NOT NULL,
            correlation_id text NOT NULL,
            action text NOT NULL,
            entity_type text NOT NULL,
            entity_id text NOT NULL,
            before_json jsonb NULL,
            after_json jsonb NULL,
            occurred_at_utc timestamptz NOT NULL
        );
        CREATE INDEX ix_audit_events_entity ON audit_events(entity_type, entity_id, occurred_at_utc);
        """),
        (2, """
        CREATE TABLE IF NOT EXISTS packaging_print_intents (
            id text PRIMARY KEY,
            packaging_unit_id text NOT NULL UNIQUE REFERENCES packaging_units(id),
            label_type text NOT NULL,
            fields_json jsonb NOT NULL,
            created_at_utc timestamptz NOT NULL
        );
        """),
        (3, """
        CREATE TABLE manufacturing_routes (
            id text PRIMARY KEY,
            order_id text NOT NULL REFERENCES production_orders(id),
            name text NOT NULL,
            route_type text NOT NULL
        );

        CREATE TABLE manufacturing_operations (
            route_id text NOT NULL REFERENCES manufacturing_routes(id),
            operation_id text NOT NULL,
            name text NOT NULL,
            sequence integer NOT NULL CHECK (sequence > 0),
            PRIMARY KEY (route_id, operation_id),
            UNIQUE (route_id, sequence)
        );

        CREATE TABLE stations (
            id text PRIMARY KEY,
            name text NOT NULL
        );

        CREATE TABLE station_qualifications (
            station_id text NOT NULL REFERENCES stations(id),
            operation_id text NOT NULL,
            PRIMARY KEY (station_id, operation_id)
        );

        ALTER TABLE packaging_bindings ADD COLUMN idempotency_key text NULL;
        ALTER TABLE packaging_bindings ADD COLUMN request_hash text NULL;
        CREATE UNIQUE INDEX ux_packaging_bindings_idempotency_key
            ON packaging_bindings(idempotency_key) WHERE idempotency_key IS NOT NULL;
        """),
        (4, """
        ALTER TABLE print_jobs ADD COLUMN claimed_by_station_id text NULL;
        ALTER TABLE print_jobs ADD COLUMN claimed_by_operator_id text NULL;
        ALTER TABLE print_jobs ADD COLUMN claim_idempotency_key text NULL;
        ALTER TABLE print_jobs ADD COLUMN claim_request_hash text NULL;
        ALTER TABLE print_jobs ADD COLUMN receipt_idempotency_key text NULL;
        ALTER TABLE print_jobs ADD COLUMN receipt_request_hash text NULL;
        CREATE UNIQUE INDEX ux_print_jobs_claim_idempotency_key
            ON print_jobs(claim_idempotency_key) WHERE claim_idempotency_key IS NOT NULL;
        CREATE UNIQUE INDEX ux_print_jobs_receipt_idempotency_key
            ON print_jobs(receipt_idempotency_key) WHERE receipt_idempotency_key IS NOT NULL;
        CREATE INDEX ix_print_jobs_claim_queue ON print_jobs(state, created_at_utc);
        """),
        (5, """
        CREATE TABLE print_job_claim_requests (
            idempotency_key text PRIMARY KEY,
            request_hash text NOT NULL,
            job_id text NULL REFERENCES print_jobs(job_id),
            created_at_utc timestamptz NOT NULL
        );
        """),
        (6, """
        ALTER TABLE packaging_units ADD COLUMN production_unit_id text NULL REFERENCES production_units(id);
        CREATE UNIQUE INDEX ux_packaging_units_production_unit
            ON packaging_units(production_unit_id) WHERE production_unit_id IS NOT NULL;
        ALTER TABLE print_jobs ADD COLUMN trace_order_id text NULL REFERENCES production_orders(id);
        ALTER TABLE print_jobs ADD COLUMN trace_unit_id text NULL REFERENCES production_units(id);
        ALTER TABLE print_jobs ADD COLUMN trace_packaging_unit_id text NULL REFERENCES packaging_units(id);
        CREATE INDEX ix_print_jobs_trace_order ON print_jobs(trace_order_id) WHERE trace_order_id IS NOT NULL;
        CREATE INDEX ix_print_jobs_trace_unit ON print_jobs(trace_unit_id) WHERE trace_unit_id IS NOT NULL;
        CREATE INDEX ix_print_jobs_trace_packaging ON print_jobs(trace_packaging_unit_id) WHERE trace_packaging_unit_id IS NOT NULL;
        """),
        (7, """
        CREATE TABLE inspection_lots (
            id text PRIMARY KEY,
            order_id text NOT NULL REFERENCES production_orders(id),
            inspection_type text NOT NULL,
            sample_rule text NOT NULL,
            sample_unit_ids_json jsonb NOT NULL,
            status text NOT NULL,
            version bigint NOT NULL CHECK (version >= 0),
            created_at_utc timestamptz NOT NULL
        );

        CREATE TABLE inspection_results (
            id text PRIMARY KEY,
            lot_id text NOT NULL REFERENCES inspection_lots(id),
            unit_id text NOT NULL REFERENCES production_units(id),
            item_code text NOT NULL,
            outcome text NOT NULL,
            defect_code text NOT NULL,
            responsible_operation_id text NOT NULL,
            remarks text NOT NULL,
            inspected_at_utc timestamptz NOT NULL,
            idempotency_key text NOT NULL UNIQUE,
            request_hash text NOT NULL,
            UNIQUE (lot_id, unit_id, item_code)
        );

        CREATE TABLE dispositions (
            id text PRIMARY KEY,
            lot_id text NOT NULL UNIQUE REFERENCES inspection_lots(id),
            decision text NOT NULL,
            reason_code text NOT NULL,
            approved_by text NOT NULL,
            approved_at_utc timestamptz NOT NULL,
            idempotency_key text NOT NULL UNIQUE,
            request_hash text NOT NULL
        );

        CREATE TABLE rework_orders (
            id text PRIMARY KEY,
            production_unit_id text NOT NULL REFERENCES production_units(id),
            route_id text NOT NULL REFERENCES manufacturing_routes(id),
            reason_code text NOT NULL,
            start_operation_id text NOT NULL,
            status text NOT NULL,
            sequence integer NOT NULL CHECK (sequence > 0),
            approved_by text NOT NULL DEFAULT '',
            approved_at_utc timestamptz NULL,
            closed_by text NOT NULL DEFAULT '',
            closed_at_utc timestamptz NULL,
            version bigint NOT NULL CHECK (version >= 0)
        );
        CREATE UNIQUE INDEX ux_rework_orders_active_unit
            ON rework_orders(production_unit_id) WHERE status IN ('Approved','Active');

        CREATE TABLE rework_order_commands (
            idempotency_key text PRIMARY KEY,
            request_hash text NOT NULL,
            rework_order_id text NOT NULL REFERENCES rework_orders(id),
            result_status text NOT NULL,
            created_at_utc timestamptz NOT NULL
        );

        CREATE TABLE shipments (
            id text PRIMARY KEY,
            order_id text NOT NULL REFERENCES production_orders(id),
            customer text NOT NULL,
            planned_quantity integer NOT NULL CHECK (planned_quantity > 0),
            delivery_reference text NOT NULL,
            status text NOT NULL,
            version bigint NOT NULL CHECK (version >= 0),
            created_at_utc timestamptz NOT NULL
        );

        CREATE TABLE shipment_items (
            shipment_id text NOT NULL REFERENCES shipments(id),
            carton_id text NOT NULL REFERENCES packaging_units(id),
            quantity integer NOT NULL CHECK (quantity > 0),
            scanned_at_utc timestamptz NOT NULL,
            operator_id text NOT NULL,
            idempotency_key text NOT NULL UNIQUE,
            request_hash text NOT NULL,
            PRIMARY KEY (shipment_id, carton_id),
            UNIQUE (carton_id)
        );

        CREATE TABLE shipment_commands (
            idempotency_key text PRIMARY KEY,
            request_hash text NOT NULL,
            shipment_id text NOT NULL REFERENCES shipments(id),
            result_status text NOT NULL,
            created_at_utc timestamptz NOT NULL
        );

        CREATE TABLE order_archive_snapshots (
            id text PRIMARY KEY,
            order_id text NOT NULL UNIQUE REFERENCES production_orders(id),
            payload_json jsonb NOT NULL,
            payload_hash text NOT NULL,
            archived_at_utc timestamptz NOT NULL,
            archived_by text NOT NULL,
            idempotency_key text NOT NULL UNIQUE,
            request_hash text NOT NULL
        );
        """),
        (8, """
        ALTER TABLE production_units ADD CONSTRAINT uq_production_units_id_order UNIQUE (id, order_id);
        ALTER TABLE inspection_lots ADD CONSTRAINT uq_inspection_lots_id_order UNIQUE (id, order_id);

        CREATE TABLE inspection_lot_samples (
            lot_id text NOT NULL,
            order_id text NOT NULL,
            unit_id text NOT NULL,
            PRIMARY KEY (lot_id, unit_id),
            FOREIGN KEY (lot_id, order_id) REFERENCES inspection_lots(id, order_id),
            FOREIGN KEY (unit_id, order_id) REFERENCES production_units(id, order_id)
        );
        CREATE TABLE IF NOT EXISTS migration_repair_records (
            id text PRIMARY KEY,
            migration_version integer NOT NULL,
            entity_type text NOT NULL,
            entity_id text NOT NULL,
            issue_code text NOT NULL,
            details_json jsonb NOT NULL,
            repaired_at_utc timestamptz NOT NULL
        );
        INSERT INTO migration_repair_records
            (id, migration_version, entity_type, entity_id, issue_code, details_json, repaired_at_utc)
        SELECT DISTINCT ON (l.id, sample.value)
               'v8-cross-order-sample:' || l.id || ':' || sample.value, 8, 'InspectionLot', l.id,
               'CROSS_ORDER_SAMPLE_REMOVED',
               jsonb_build_object('lotOrderId', l.order_id, 'removedUnitId', sample.value,
                                  'unitOrderId', u.order_id), now()
        FROM inspection_lots l
        CROSS JOIN LATERAL jsonb_array_elements_text(l.sample_unit_ids_json) sample(value)
        LEFT JOIN production_units u ON u.id=sample.value
        WHERE u.id IS NULL OR u.order_id<>l.order_id
        ON CONFLICT (id) DO NOTHING;
        INSERT INTO audit_events
            (id, actor_id, station_id, shift_id, correlation_id, action, entity_type, entity_id,
             before_json, after_json, occurred_at_utc)
        SELECT DISTINCT ON (l.id, sample.value)
               'v8-cross-order-sample:' || l.id || ':' || sample.value,
               'schema-migrator', 'system', 'migration', 'migration-v8',
               'InvalidInspectionSampleRemoved', 'InspectionLot', l.id,
               jsonb_build_object('sampleUnitId', sample.value),
               jsonb_build_object('reason', 'CROSS_ORDER_SAMPLE_REMOVED'), now()
        FROM inspection_lots l
        CROSS JOIN LATERAL jsonb_array_elements_text(l.sample_unit_ids_json) sample(value)
        LEFT JOIN production_units u ON u.id=sample.value
        WHERE u.id IS NULL OR u.order_id<>l.order_id
        ON CONFLICT (id) DO NOTHING;
        INSERT INTO migration_repair_records
            (id, migration_version, entity_type, entity_id, issue_code, details_json, repaired_at_utc)
        SELECT 'v8-cross-order-result:' || r.id, 8, 'InspectionResult', r.id,
               'CROSS_ORDER_RESULT_REMOVED',
               jsonb_build_object('lotId', r.lot_id, 'unitId', r.unit_id), now()
        FROM inspection_results r
        JOIN inspection_lots l ON l.id=r.lot_id
        JOIN production_units u ON u.id=r.unit_id
        WHERE u.order_id<>l.order_id
        ON CONFLICT (id) DO NOTHING;
        INSERT INTO audit_events
            (id, actor_id, station_id, shift_id, correlation_id, action, entity_type, entity_id,
             before_json, after_json, occurred_at_utc)
        SELECT 'v8-cross-order-result:' || r.id, 'schema-migrator', 'system', 'migration', 'migration-v8',
               'InvalidInspectionResultRemoved', 'InspectionResult', r.id,
               jsonb_build_object('lotId', r.lot_id, 'unitId', r.unit_id),
               jsonb_build_object('reason', 'CROSS_ORDER_RESULT_REMOVED'), now()
        FROM inspection_results r
        JOIN inspection_lots l ON l.id=r.lot_id
        JOIN production_units u ON u.id=r.unit_id
        WHERE u.order_id<>l.order_id
        ON CONFLICT (id) DO NOTHING;
        DELETE FROM inspection_results r USING inspection_lots l, production_units u
        WHERE r.lot_id=l.id AND r.unit_id=u.id AND u.order_id<>l.order_id;
        UPDATE inspection_lots l SET sample_unit_ids_json=COALESCE((
            SELECT jsonb_agg(valid.value ORDER BY valid.first_ordinality)
            FROM (
                SELECT sample.value, min(sample.ordinality) AS first_ordinality
                FROM jsonb_array_elements_text(l.sample_unit_ids_json) WITH ORDINALITY sample(value, ordinality)
                JOIN production_units u ON u.id=sample.value AND u.order_id=l.order_id
                GROUP BY sample.value
            ) valid
        ), '[]'::jsonb);
        INSERT INTO inspection_lot_samples(lot_id, order_id, unit_id)
        SELECT l.id, l.order_id, sample.value
        FROM inspection_lots l
        CROSS JOIN LATERAL jsonb_array_elements_text(l.sample_unit_ids_json) sample(value);
        ALTER TABLE inspection_results ADD CONSTRAINT fk_inspection_results_sample
            FOREIGN KEY (lot_id, unit_id) REFERENCES inspection_lot_samples(lot_id, unit_id);

        CREATE TABLE packaging_quality_holds (
            lot_id text NOT NULL REFERENCES inspection_lots(id),
            packaging_unit_id text NOT NULL REFERENCES packaging_units(id),
            previous_status text NOT NULL,
            PRIMARY KEY (lot_id, packaging_unit_id)
        );

        CREATE TABLE inspection_lot_commands (
            idempotency_key text PRIMARY KEY,
            request_hash text NOT NULL,
            lot_id text NOT NULL REFERENCES inspection_lots(id),
            result_status text NOT NULL,
            created_at_utc timestamptz NOT NULL
        );

        ALTER TABLE rework_orders ADD COLUMN order_id text NULL;
        UPDATE rework_orders r SET order_id=u.order_id FROM production_units u WHERE u.id=r.production_unit_id;
        ALTER TABLE rework_orders ALTER COLUMN order_id SET NOT NULL;
        ALTER TABLE rework_orders ADD CONSTRAINT fk_rework_order_unit
            FOREIGN KEY (production_unit_id, order_id) REFERENCES production_units(id, order_id);
        ALTER TABLE manufacturing_routes ADD CONSTRAINT uq_manufacturing_routes_id_order UNIQUE (id, order_id);
        ALTER TABLE rework_orders ADD CONSTRAINT fk_rework_order_route
            FOREIGN KEY (route_id, order_id) REFERENCES manufacturing_routes(id, order_id);
        ALTER TABLE rework_orders ADD CONSTRAINT fk_rework_start_operation
            FOREIGN KEY (route_id, start_operation_id) REFERENCES manufacturing_operations(route_id, operation_id);
        ALTER TABLE rework_orders ADD CONSTRAINT uq_rework_pass_context
            UNIQUE (id, production_unit_id, route_id, sequence);
        ALTER TABLE station_pass_records ALTER COLUMN rework_order_id DROP NOT NULL;
        UPDATE station_pass_records SET rework_order_id=NULL WHERE rework_order_id='';
        ALTER TABLE station_pass_records ADD CONSTRAINT fk_station_pass_rework_context
            FOREIGN KEY (rework_order_id, unit_id, route_id, rework_sequence)
            REFERENCES rework_orders(id, production_unit_id, route_id, sequence);

        CREATE OR REPLACE FUNCTION prevent_order_archive_mutation() RETURNS trigger AS $$
        BEGIN
            RAISE EXCEPTION 'order archive snapshots are immutable' USING ERRCODE = '55000';
        END;
        $$ LANGUAGE plpgsql;
        CREATE TRIGGER order_archive_snapshots_immutable
            BEFORE UPDATE OR DELETE ON order_archive_snapshots
            FOR EACH ROW EXECUTE FUNCTION prevent_order_archive_mutation();
        """),
        (9, """
        CREATE TABLE IF NOT EXISTS migration_repair_records (
            id text PRIMARY KEY,
            migration_version integer NOT NULL,
            entity_type text NOT NULL,
            entity_id text NOT NULL,
            issue_code text NOT NULL,
            details_json jsonb NOT NULL,
            repaired_at_utc timestamptz NOT NULL
        );
        INSERT INTO migration_repair_records
            (id, migration_version, entity_type, entity_id, issue_code, details_json, repaired_at_utc)
        SELECT DISTINCT ON (l.id, sample.value)
               'v9-cross-order-sample:' || l.id || ':' || sample.value, 9, 'InspectionLot', l.id,
               'CROSS_ORDER_SAMPLE_REMOVED',
               jsonb_build_object('lotOrderId', l.order_id, 'removedUnitId', sample.value,
                                  'unitOrderId', u.order_id), now()
        FROM inspection_lots l
        CROSS JOIN LATERAL jsonb_array_elements_text(l.sample_unit_ids_json) sample(value)
        LEFT JOIN production_units u ON u.id=sample.value
        WHERE u.id IS NULL OR u.order_id<>l.order_id
        ON CONFLICT (id) DO NOTHING;
        INSERT INTO audit_events
            (id, actor_id, station_id, shift_id, correlation_id, action, entity_type, entity_id,
             before_json, after_json, occurred_at_utc)
        SELECT DISTINCT ON (l.id, sample.value)
               'v9-cross-order-sample:' || l.id || ':' || sample.value,
               'schema-migrator', 'system', 'migration', 'migration-v9',
               'InvalidInspectionSampleRemoved', 'InspectionLot', l.id,
               jsonb_build_object('sampleUnitId', sample.value),
               jsonb_build_object('reason', 'CROSS_ORDER_SAMPLE_REMOVED'), now()
        FROM inspection_lots l
        CROSS JOIN LATERAL jsonb_array_elements_text(l.sample_unit_ids_json) sample(value)
        LEFT JOIN production_units u ON u.id=sample.value
        WHERE u.id IS NULL OR u.order_id<>l.order_id
        ON CONFLICT (id) DO NOTHING;
        UPDATE inspection_lots l SET sample_unit_ids_json=COALESCE((
            SELECT jsonb_agg(valid.value ORDER BY valid.first_ordinality)
            FROM (
                SELECT sample.value, min(sample.ordinality) AS first_ordinality
                FROM jsonb_array_elements_text(l.sample_unit_ids_json) WITH ORDINALITY sample(value, ordinality)
                JOIN production_units u ON u.id=sample.value AND u.order_id=l.order_id
                GROUP BY sample.value
            ) valid
        ), '[]'::jsonb);
        DROP TRIGGER IF EXISTS order_archive_snapshots_immutable ON order_archive_snapshots;
        """)
    };

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock($1)", cancellationToken, MigrationLockId);
        await ExecuteAsync(connection, transaction,
            "CREATE TABLE IF NOT EXISTS schema_migrations (version integer PRIMARY KEY, applied_at_utc timestamptz NOT NULL)", cancellationToken);

        foreach (var migration in Migrations)
        {
            await using var exists = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE version=$1)", connection, transaction);
            exists.Parameters.AddWithValue(migration.Version);
            if ((bool)(await exists.ExecuteScalarAsync(cancellationToken) ?? false)) continue;
            await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken);
            if (migration.Version == 9)
                await RepairArchiveHashesAsync(connection, transaction, cancellationToken);
            await ExecuteAsync(connection, transaction,
                "INSERT INTO schema_migrations(version, applied_at_utc) VALUES ($1, $2)", cancellationToken,
                migration.Version, DateTimeOffset.UtcNow);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RepairArchiveHashesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var archives = new List<(string Id, string Payload)>();
        await using (var read = new NpgsqlCommand(
            "SELECT id, payload_json::text FROM order_archive_snapshots", connection, transaction))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) archives.Add((reader.GetString(0), reader.GetString(1)));
        foreach (var archive in archives)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(archive.Payload))).ToLowerInvariant();
            await ExecuteAsync(connection, transaction,
                "UPDATE order_archive_snapshots SET payload_hash=$1 WHERE id=$2", cancellationToken, hash, archive.Id);
        }
        await ExecuteAsync(connection, transaction, """
            CREATE TRIGGER order_archive_snapshots_immutable
                BEFORE UPDATE OR DELETE ON order_archive_snapshots
                FOR EACH ROW EXECUTE FUNCTION prevent_order_archive_mutation()
            """, cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, CancellationToken cancellationToken, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        for (var index = 0; index < values.Length; index++) command.Parameters.AddWithValue(values[index]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
