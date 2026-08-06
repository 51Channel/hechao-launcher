ALTER TABLE launcher.package_imports
    ADD COLUMN publisher_progress_phase text,
    ADD COLUMN publisher_progress_completed_objects integer,
    ADD COLUMN publisher_progress_total_objects integer,
    ADD COLUMN publisher_progress_processed_bytes bigint,
    ADD COLUMN publisher_progress_total_bytes bigint,
    ADD COLUMN publisher_progress_sampled_at timestamp with time zone,
    ADD CONSTRAINT package_imports_publisher_progress_phase_check CHECK (
        publisher_progress_phase IS NULL OR publisher_progress_phase IN (
            'DownloadingArchive', 'ExtractingArchive', 'BuildingDistribution',
            'PublishingObjects', 'Finalizing'
        )
    ),
    ADD CONSTRAINT package_imports_publisher_progress_values_check CHECK (
        (publisher_progress_phase IS NULL
         AND publisher_progress_completed_objects IS NULL
         AND publisher_progress_total_objects IS NULL
         AND publisher_progress_processed_bytes IS NULL
         AND publisher_progress_total_bytes IS NULL
         AND publisher_progress_sampled_at IS NULL)
        OR
        (publisher_progress_phase IS NOT NULL
         AND publisher_progress_completed_objects BETWEEN 0 AND publisher_progress_total_objects
         AND publisher_progress_total_objects BETWEEN 0 AND 200000
         AND publisher_progress_processed_bytes BETWEEN 0 AND publisher_progress_total_bytes
         AND publisher_progress_total_bytes >= 0
         AND publisher_progress_sampled_at IS NOT NULL)
    );
