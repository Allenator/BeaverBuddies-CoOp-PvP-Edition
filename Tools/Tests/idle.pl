#!/usr/bin/perl
# idle.pl <seconds> <host logs...>: the stretches where a scenario sent its copy no command for
# that long or longer, longest first, with the commands either side: the waits that slow a run
use strict; use warnings;
my $min = shift; my @gaps;
for my $file (@ARGV) {
    open my $h, '<', $file or next;
    my ($was, $last);
    while (<$h>) {
        next unless /^\[(\d\d)-(\d\d)-(\d\d(?:\.\d+)?)\] \[Test\] (.*)/;
        my $t = $1 * 3600 + $2 * 60 + $3;
        my $line = substr($4, 0, 100);
        if (defined $was) {
            my $d = $t - $was; $d += 86400 if $d < 0;
            # A match loading isn't waiting
            push @gaps, [$d, $file =~ s{.*/}{}r =~ s{-host-\d+\.log$}{}r, $last, $line] if $d >= $min && $last !~ /^(hostmatch|hostnew|newgame|rehost|joinmatch)\b/;
        }
        ($was, $last) = ($t, $line);
    }
}
printf "- %d s in %s, after `%s`, before `%s`\n", @$_ for sort { $b->[0] <=> $a->[0] } @gaps;
