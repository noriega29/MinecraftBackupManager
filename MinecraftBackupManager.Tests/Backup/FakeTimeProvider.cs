using System.Diagnostics;

namespace MinecraftBackupManager.Tests.Backup
{
    /// <summary>
    /// Reloj controlado por las pruebas: la hora solo cambia cuando se le ordena,
    /// y los temporizadores que crea solo vencen al adelantar el reloj con Advance.
    /// </summary>
    internal sealed class FakeTimeProvider : TimeProvider
    {
        private readonly object _lock = new();
        private readonly List<FakeTimer> _timers = new();

        private DateTimeOffset _utcNow;

        // Por defecto es UTC, para que el "día local" no dependa del equipo que ejecuta las pruebas.
        private TimeZoneInfo _localTimeZone = TimeZoneInfo.Utc;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock)
            {
                return _utcNow;
            }
        }

        public override TimeZoneInfo LocalTimeZone
        {
            get
            {
                lock (_lock)
                {
                    return _localTimeZone;
                }
            }
        }

        /// <summary>Cuántos temporizadores están esperando a vencer.</summary>
        public int ActiveTimerCount
        {
            get
            {
                lock (_lock)
                {
                    return _timers.Count(timer => timer.DueTime is not null);
                }
            }
        }

        // Cambian la hora sin disparar temporizadores.
        public void SetUtcNow(DateTimeOffset utcNow)
        {
            lock (_lock)
            {
                _utcNow = utcNow;
            }
        }

        public void SetLocalTimeZone(TimeZoneInfo timeZone)
        {
            lock (_lock)
            {
                _localTimeZone = timeZone;
            }
        }

        public void AdvanceDays(int days)
        {
            lock (_lock)
            {
                _utcNow = _utcNow.AddDays(days);
            }
        }

        /// <summary>
        /// Adelanta el reloj y dispara, en orden, los temporizadores que venzan. El reloj se
        /// detiene en el instante exacto de cada vencimiento mientras se ejecuta su función.
        /// </summary>
        public void Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "No se puede retroceder el reloj.");
            }

            DateTimeOffset target;

            lock (_lock)
            {
                target = _utcNow + delta;
            }

            while (true)
            {
                FakeTimer? due;

                lock (_lock)
                {
                    due = _timers
                        .Where(timer => timer.DueTime is not null && timer.DueTime <= target)
                        .OrderBy(timer => timer.DueTime)
                        .FirstOrDefault();

                    if (due is null)
                    {
                        _utcNow = target;

                        return;
                    }

                    if (due.DueTime!.Value > _utcNow)
                    {
                        _utcNow = due.DueTime.Value;
                    }

                    // Un temporizador de un solo disparo se desactiva; uno periódico se reprograma.
                    due.DueTime = due.Period > TimeSpan.Zero ? due.DueTime + due.Period : null;
                }

                // Fuera del candado: la función puede crear otros temporizadores.
                due.Invoke();
            }
        }

        /// <summary>Espera (en tiempo real) a que alguien cree un temporizador.</summary>
        public async Task WaitForActiveTimerAsync(TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();

            while (ActiveTimerCount == 0)
            {
                if (stopwatch.Elapsed > timeout)
                {
                    throw new TimeoutException("Nadie creó un temporizador en el reloj falso.");
                }

                await Task.Delay(10);
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);

            var timer = new FakeTimer(this, callback, state);

            lock (_lock)
            {
                _timers.Add(timer);
            }

            timer.Change(dueTime, period);

            return timer;
        }

        private sealed class FakeTimer : ITimer
        {
            private readonly FakeTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;

            public FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
            }

            // Instante (UTC) en que vence, o null si está desactivado. Se toca con el candado del reloj.
            public DateTimeOffset? DueTime { get; set; }

            public TimeSpan Period { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_owner._lock)
                {
                    DueTime = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._utcNow + dueTime;
                    Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;

                    return true;
                }
            }

            public void Invoke()
            {
                _callback(_state);
            }

            public void Dispose()
            {
                lock (_owner._lock)
                {
                    DueTime = null;
                    _owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }
        }
    }
}
