import { useEffect, useRef, useState } from 'react';
import { percent } from '../lib/format';
import { useT } from '../lib/i18n';
import { breakEven, clockLabel, eventsIn, nearestIndex, periodOf, position, scaleFor } from '../lib/winChart';
import type { WinReading } from '../lib/winChart';

interface Props {
    history: WinReading[];
    /** The price taken, which sets the break-even line. */
    entryOdds: number;
    /** Where the bet was struck, as a marker. Null minute: before kick-off. Null chance: not known. */
    entryMinute: number | null;
    entryProbability: number | null;
}

const HEIGHT = 230;
const MARGIN = { top: 22, right: 46, bottom: 26, left: 38 };
const GRID = [0, 0.25, 0.5, 0.75, 1];
/** Below this a stoppage band is too narrow to carry its "+3" without colliding with a tick. */
const BAND_LABEL_MIN_PX = 16;

/**
 * The win chance of one open bet across the match, with the things that moved it marked on it.
 *
 * The line carries the only data; the break-even line, the entry point, the returned-stake band and
 * the red-card marks are references, named once in the key above the plot rather than labelled where
 * they are drawn — there they collide with the line exactly when the line is near them, which is
 * when they matter. Goals carry their scoreline, which needs no key. The minute axis lays
 * the halves end to end with each half's stoppage as its own shaded band, which is where the
 * referee's added time becomes visible: a band appears when the board goes up, as wide as the board.
 *
 * Drawn in pixels measured off the container rather than in a scaled viewBox, so the text stays the
 * size of the text around it on a phone instead of shrinking with the chart.
 */
export function WinProbabilityChart({ history, entryOdds, entryMinute, entryProbability }: Props) {
    const t = useT();
    const container = useRef<HTMLDivElement | null>(null);
    const [width, setWidth] = useState(0);
    const [focus, setFocus] = useState<number | null>(null);

    useEffect(() => {
        const node = container.current;
        if (!node) return undefined;

        setWidth(node.clientWidth);
        const observer = new ResizeObserver(([entry]) => setWidth(entry.contentRect.width));
        observer.observe(node);
        return () => observer.disconnect();
    }, []);

    const scale = scaleFor(history);
    const plotWidth = Math.max(0, width - MARGIN.left - MARGIN.right);
    const plotHeight = HEIGHT - MARGIN.top - MARGIN.bottom;
    const x = (chartMinute: number) => MARGIN.left + (chartMinute / scale.end) * plotWidth;
    const y = (probability: number) => MARGIN.top + (1 - probability) * plotHeight;

    const points = history.map((reading) => ({ reading, px: x(position(reading, scale)), py: y(reading.win) }));
    const last = points[points.length - 1];
    const line = points.map((p) => `${p.px.toFixed(1)},${p.py.toFixed(1)}`).join(' ');
    const anyPush = history.some((reading) => reading.push > 0);
    const pushBand = anyPush
        ? [
              ...points.map((p) => `${p.px.toFixed(1)},${y(Math.min(1, p.reading.win + p.reading.push)).toFixed(1)}`),
              ...[...points].reverse().map((p) => `${p.px.toFixed(1)},${p.py.toFixed(1)}`),
          ].join(' ')
        : '';

    const threshold = breakEven(entryOdds);
    const events = eventsIn(history, scale);
    const halfTimeX = x(45 + scale.firstStoppage);
    const ticks = [
        ...[0, 15, 30, 45].map((minute) => ({ minute, at: minute })),
        ...[60, 75, 90].map((minute) => ({ minute, at: minute + scale.firstStoppage })),
    ];
    const bands = [
        { from: 45, width: scale.firstStoppage, board: scale.boardFirst },
        { from: 90 + scale.firstStoppage, width: scale.secondStoppage, board: scale.boardSecond },
    ].filter((band) => band.width > 0);

    const entry =
        entryProbability === null
            ? null
            : {
                  px: x(entryMinute === null ? 0 : position({ minute: entryMinute, period: periodOf(entryMinute, null) }, scale)),
                  py: y(entryProbability),
              };

    const focused = focus === null ? null : points[focus] ?? null;
    const anyRed = events.some((event) => event.kind === 'red');

    const pointAt = (clientX: number) => {
        const box = container.current?.getBoundingClientRect();
        if (!box || plotWidth <= 0) return;
        const chartMinute = ((clientX - box.left - MARGIN.left) / plotWidth) * scale.end;
        setFocus(nearestIndex(history, scale, chartMinute));
    };

    const step = (by: number) => {
        if (history.length === 0) return;
        const from = focus ?? (by > 0 ? -1 : history.length);
        setFocus(Math.min(history.length - 1, Math.max(0, from + by)));
    };

    const summary =
        last === undefined
            ? t.chart.empty
            : t.chart.summary(
                  percent(last.reading.win),
                  clockLabel(last.reading, t.chart.halfTime),
                  entryProbability === null ? null : percent(entryProbability),
                  percent(threshold),
              );

    return (
        <figure className="win-chart">
            <ul className="win-chart-key">
                <li>
                    <span className="key-line" aria-hidden="true" />
                    {t.chart.keyWin}
                </li>
                <li>
                    <span className="key-threshold" aria-hidden="true" />
                    {t.chart.breakEven(percent(threshold, 0))}
                </li>
                {entry && entryProbability !== null && (
                    <li>
                        <span className="key-entry" aria-hidden="true" />
                        {t.chart.keyEntry(percent(entryProbability, 0))}
                    </li>
                )}
                {anyPush && (
                    <li>
                        <span className="key-push" aria-hidden="true" />
                        {t.chart.push}
                    </li>
                )}
                {anyRed && (
                    <li>
                        <span className="key-red" aria-hidden="true" />
                        {t.chart.keyRed}
                    </li>
                )}
            </ul>

            <div className="win-chart-frame" ref={container}>
                {width > 0 && (
                    <svg
                        width={width}
                        height={HEIGHT}
                        role="img"
                        aria-label={summary}
                        tabIndex={0}
                        onPointerMove={(event) => pointAt(event.clientX)}
                        onPointerDown={(event) => pointAt(event.clientX)}
                        onPointerLeave={() => setFocus(null)}
                        onBlur={() => setFocus(null)}
                        onKeyDown={(event) => {
                            if (event.key === 'ArrowRight') step(1);
                            else if (event.key === 'ArrowLeft') step(-1);
                            else if (event.key === 'Escape') setFocus(null);
                            else return;
                            event.preventDefault();
                        }}
                    >
                        {bands.map((band) => {
                            const left = x(band.from);
                            const right = x(band.from + band.width);
                            return (
                                <g key={band.from}>
                                    <rect
                                        className="win-chart-band"
                                        x={left}
                                        y={MARGIN.top}
                                        width={right - left}
                                        height={plotHeight}
                                    />
                                    {right - left >= BAND_LABEL_MIN_PX && (
                                        <text className="win-chart-band-label" x={(left + right) / 2} y={MARGIN.top + 12}>
                                            +{band.board ?? band.width}
                                        </text>
                                    )}
                                </g>
                            );
                        })}

                        {GRID.map((p) => (
                            <g key={p}>
                                <line
                                    className="win-chart-grid"
                                    x1={MARGIN.left}
                                    x2={MARGIN.left + plotWidth}
                                    y1={y(p)}
                                    y2={y(p)}
                                />
                                <text className="win-chart-axis" x={MARGIN.left - 6} y={y(p) + 4} textAnchor="end">
                                    {Math.round(p * 100)}%
                                </text>
                            </g>
                        ))}

                        <line
                            className="win-chart-halftime"
                            x1={halfTimeX}
                            x2={halfTimeX}
                            y1={MARGIN.top}
                            y2={MARGIN.top + plotHeight}
                        />

                        {ticks.map((tick) => (
                            <text
                                key={tick.minute}
                                className="win-chart-axis"
                                x={x(tick.at)}
                                y={HEIGHT - 8}
                                textAnchor={tick.minute === 0 ? 'start' : 'middle'}
                            >
                                {tick.minute}&apos;
                            </text>
                        ))}

                        {events.map((event, index) => {
                            const px = x(event.x);
                            return (
                                <g key={`${event.kind}-${event.x}-${index}`} className={`win-chart-event win-chart-${event.kind}`}>
                                    <line x1={px} x2={px} y1={MARGIN.top} y2={MARGIN.top + plotHeight} />
                                    {event.kind === 'goal' ? (
                                        <text x={px} y={MARGIN.top - 7} textAnchor="middle">
                                            {event.score}
                                        </text>
                                    ) : (
                                        <rect x={px - 3} y={MARGIN.top - 15} width={6} height={9} rx={1} />
                                    )}
                                </g>
                            );
                        })}

                        <line
                            className="win-chart-threshold"
                            x1={MARGIN.left}
                            x2={MARGIN.left + plotWidth}
                            y1={y(threshold)}
                            y2={y(threshold)}
                        />

                        {anyPush && <polygon className="win-chart-push" points={pushBand} />}

                        {points.length > 1 && <polyline className="win-chart-line" points={line} />}

                        {entry && (
                            <circle className="win-chart-entry" cx={entry.px} cy={entry.py} r={5} />
                        )}

                        {last && (
                            <g>
                                <circle className="win-chart-dot" cx={last.px} cy={last.py} r={4} />
                                <text className="win-chart-end-label" x={last.px + 8} y={last.py + 4}>
                                    {percent(last.reading.win, 0)}
                                </text>
                            </g>
                        )}

                        {focused && (
                            <g className="win-chart-focus">
                                <line x1={focused.px} x2={focused.px} y1={MARGIN.top} y2={MARGIN.top + plotHeight} />
                                <circle cx={focused.px} cy={focused.py} r={5} />
                            </g>
                        )}
                    </svg>
                )}

                {focused && (
                    <div
                        className="win-chart-tip"
                        style={{
                            left: Math.min(Math.max(focused.px - 80, 0), Math.max(0, width - 170)),
                            top: Math.max(0, focused.py > HEIGHT / 2 ? MARGIN.top : HEIGHT / 2),
                        }}
                    >
                        <strong>{percent(focused.reading.win)}</strong> {t.chart.win}
                        {focused.reading.push > 0 && (
                            <div>
                                <strong>{percent(focused.reading.push)}</strong> {t.chart.push}
                            </div>
                        )}
                        <div className="win-chart-tip-sub">
                            {clockLabel(focused.reading, t.chart.halfTime)} · {focused.reading.goalsHome}-
                            {focused.reading.goalsAway}
                            {(focused.reading.redCardsHome > 0 || focused.reading.redCardsAway > 0) &&
                                ` · ${t.chart.reds(focused.reading.redCardsHome, focused.reading.redCardsAway)}`}
                        </div>
                        <div className="win-chart-tip-sub">{t.chart.left(Math.round(focused.reading.remainingMinutes))}</div>
                    </div>
                )}
            </div>

            <figcaption className="hint">{summary}</figcaption>

            {history.length > 0 && (
                <details className="win-chart-table">
                    <summary>{t.chart.table(history.length)}</summary>
                    <table>
                        <thead>
                            <tr>
                                <th>{t.chart.minute}</th>
                                <th>{t.chart.score}</th>
                                <th className="numeric">{t.chart.win}</th>
                                {anyPush && <th className="numeric">{t.chart.push}</th>}
                                <th className="numeric">{t.chart.remaining}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {[...history].reverse().map((reading) => (
                                <tr key={`${reading.period}-${reading.minute}`}>
                                    <td>{clockLabel(reading, t.chart.halfTime)}</td>
                                    <td>
                                        {reading.goalsHome}-{reading.goalsAway}
                                    </td>
                                    <td className="numeric">{percent(reading.win)}</td>
                                    {anyPush && <td className="numeric">{percent(reading.push)}</td>}
                                    <td className="numeric">{Math.round(reading.remainingMinutes)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </details>
            )}
        </figure>
    );
}
