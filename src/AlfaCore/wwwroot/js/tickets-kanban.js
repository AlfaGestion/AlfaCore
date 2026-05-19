const wiredBoards = new WeakMap();

export function wireKanban(root, dotNetRef) {
    if (!root || !dotNetRef) {
        return;
    }

    const previous = wiredBoards.get(root);
    if (previous) {
        previous();
    }

    let draggingId = '';
    const cleanups = [];

    const on = (element, eventName, handler) => {
        element.addEventListener(eventName, handler);
        cleanups.push(() => element.removeEventListener(eventName, handler));
    };

    root.querySelectorAll('.tickets-card[data-ticket-id]').forEach((card) => {
        on(card, 'dragstart', (event) => {
            draggingId = card.dataset.ticketId || '';
            card.classList.add('tickets-card--dragging');
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', draggingId);
        });

        on(card, 'dragend', () => {
            draggingId = '';
            card.classList.remove('tickets-card--dragging');
            clearTargets(root);
        });
    });

    root.querySelectorAll('.tickets-kanban-column[data-ticket-state]').forEach((column) => {
        on(column, 'dragenter', () => {
            clearTargets(root);
            column.classList.add('tickets-kanban-column--drag-target');
        });

        on(column, 'dragover', (event) => {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
        });

        on(column, 'dragleave', (event) => {
            if (!column.contains(event.relatedTarget)) {
                column.classList.remove('tickets-kanban-column--drag-target');
            }
        });

        on(column, 'drop', async (event) => {
            event.preventDefault();
            event.stopPropagation();

            const ticketId = Number(event.dataTransfer.getData('text/plain') || draggingId);
            const targetState = column.dataset.ticketState || '';
            clearTargets(root);
            draggingId = '';

            if (!ticketId || !targetState) {
                return;
            }

            await dotNetRef.invokeMethodAsync('MoveTicketFromKanbanAsync', ticketId, targetState);
        });
    });

    wiredBoards.set(root, () => {
        cleanups.forEach((cleanup) => cleanup());
        wiredBoards.delete(root);
    });
}

function clearTargets(root) {
    root.querySelectorAll('.tickets-kanban-column--drag-target').forEach((column) => {
        column.classList.remove('tickets-kanban-column--drag-target');
    });
}
