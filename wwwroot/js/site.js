// SignalR connection for real-time job updates
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/download")
    .withAutomaticReconnect()
    .build();

const statusEl = document.getElementById('connection-status');

function setConnectionStatus(status, cssClass) {
    if (statusEl) {
        statusEl.innerHTML = `<span class="badge ${cssClass}"><i class="bi bi-circle-fill"></i> ${status}</span>`;
    }
}

connection.onreconnecting(() => setConnectionStatus('Reconnecting...', 'bg-warning text-dark'));
connection.onreconnected(() => setConnectionStatus('Connected', 'bg-success'));
connection.onclose(() => setConnectionStatus('Disconnected', 'bg-danger'));

// ── Event handlers ────────────────────────────────────────────────────────────

connection.on("JobCreated", (job) => {
    const container = document.getElementById('jobs-container');
    if (!container) return;

    // Remove empty state message if present
    const empty = container.querySelector('.text-center.text-muted');
    if (empty) empty.remove();

    const card = buildJobCard(job);
    container.insertAdjacentHTML('afterbegin', card);
});

connection.on("JobProgressUpdated", (jobId, bytes, pct, status) => {
    const card = document.getElementById(`job-${jobId}`);
    if (!card) return;

    const bytesEl = card.querySelector('.bytes-display');
    if (bytesEl) bytesEl.textContent = formatBytes(bytes);

    const bar = card.querySelector('.progress-bar');
    if (bar && pct !== null) {
        bar.style.width = `${pct.toFixed(0)}%`;
        bar.classList.remove('bg-info');
    }
});

connection.on("JobStatusChanged", (jobId, status, errorMessage) => {
    const card = document.getElementById(`job-${jobId}`);
    if (!card) return;

    card.dataset.status = status.toLowerCase();

    const badge = card.querySelector('.badge.job-status');
    if (badge) {
        badge.textContent = status;
        badge.className = `badge job-status ms-1 ${statusBadgeClass(status)}`;
    }

    if (status === 'Cancelled' || status === 'Failed' || status === 'Completed') {
        const cancelBtn = card.querySelector('.cancel-btn');
        if (cancelBtn) cancelBtn.remove();

        const bar = card.querySelector('.progress');
        if (bar && status !== 'Completed') bar.remove();
    }

    if (errorMessage) {
        const existing = card.querySelector('.text-danger.error-msg');
        if (existing) existing.textContent = errorMessage;
        else card.querySelector('.card-body').insertAdjacentHTML('beforeend',
            `<small class="text-danger error-msg">${escapeHtml(errorMessage)}</small>`);
    }
});

connection.on("JobCompleted", (jobId, outputPath) => {
    const card = document.getElementById(`job-${jobId}`);
    if (!card) return;

    const bar = card.querySelector('.progress-bar');
    if (bar) {
        bar.style.width = '100%';
        bar.classList.remove('progress-bar-animated', 'bg-info');
        bar.classList.add('bg-success');
    }
});

// ── Helpers ───────────────────────────────────────────────────────────────────

function cancelJob(jobId) {
    if (!confirm('Cancel this download?')) return;
    connection.invoke("CancelJob", jobId).catch(console.error);
}

function statusBadgeClass(status) {
    const map = {
        'Queued': 'bg-secondary',
        'Downloading': 'bg-primary',
        'Muxing': 'bg-info text-dark',
        'Completed': 'bg-success',
        'Failed': 'bg-danger',
        'Cancelled': 'bg-warning text-dark'
    };
    return map[status] ?? 'bg-secondary';
}

function buildJobCard(job) {
    const ts = new Date(job.startedAt).toLocaleString(undefined, { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' });
    return `
<div class="card mb-2 job-card" id="job-${job.id}" data-status="${job.status.toLowerCase()}">
    <div class="card-body py-2 px-3">
        <div class="d-flex justify-content-between align-items-center">
            <div>
                <strong>${escapeHtml(job.streamerLogin)}</strong>
                <span class="badge job-status ms-1 ${statusBadgeClass(job.status)}">${job.status}</span>
                <span class="badge bg-secondary ms-1">${job.type}</span>
                <small class="text-muted ms-2">${escapeHtml(job.title)}</small>
            </div>
            <div class="d-flex align-items-center gap-2">
                <small class="text-muted">${ts}</small>
                <small class="text-muted bytes-display"></small>
                <button class="btn btn-sm btn-outline-danger cancel-btn" onclick="cancelJob(${job.id})">
                    <i class="bi bi-x-circle"></i> Cancel
                </button>
            </div>
        </div>
        <div class="progress mt-1" style="height:6px">
            <div class="progress-bar progress-bar-striped progress-bar-animated bg-info"
                 role="progressbar" style="width:100%"></div>
        </div>
    </div>
</div>`;
}

function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
    return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function escapeHtml(s) {
    if (!s) return '';
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// ── Start connection ──────────────────────────────────────────────────────────

async function startConnection() {
    try {
        await connection.start();
        setConnectionStatus('Connected', 'bg-success');
    } catch (err) {
        setConnectionStatus('Disconnected', 'bg-danger');
        setTimeout(startConnection, 5000);
    }
}

startConnection();
