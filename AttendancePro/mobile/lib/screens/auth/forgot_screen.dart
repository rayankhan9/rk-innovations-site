import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../services/api.dart';
import '../../state/app_state.dart';
import '../../widgets/common.dart';

class ForgotScreen extends StatefulWidget {
  const ForgotScreen({super.key});
  @override
  State<ForgotScreen> createState() => _ForgotScreenState();
}

class _ForgotScreenState extends State<ForgotScreen> {
  final _email = TextEditingController(), _code = TextEditingController(), _pw = TextEditingController();
  bool _sent = false, _busy = false;

  Future<void> _run(Future<void> Function() f, String ok, {bool done = false}) async {
    setState(() => _busy = true);
    try {
      await f();
      if (!mounted) return;
      toast(context, ok);
      if (done) {
        Navigator.pop(context);
      } else {
        setState(() => _sent = true);
      }
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message, error: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final api = context.read<AppState>().api;
    return Scaffold(
      appBar: AppBar(title: const Text('Reset password')),
      body: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(_sent ? 'Enter the 6-digit code we emailed you and choose a new password.' : 'We\'ll email you a 6-digit code.'),
          const SizedBox(height: 16),
          TextField(controller: _email, enabled: !_sent, keyboardType: TextInputType.emailAddress, decoration: const InputDecoration(labelText: 'Email')),
          if (_sent) ...[
            const SizedBox(height: 14),
            TextField(controller: _code, keyboardType: TextInputType.number, maxLength: 6, decoration: const InputDecoration(labelText: '6-digit code', counterText: '')),
            const SizedBox(height: 14),
            TextField(controller: _pw, obscureText: true, decoration: const InputDecoration(labelText: 'New password')),
          ],
          const SizedBox(height: 20),
          FilledButton(
            onPressed: _busy
                ? null
                : () => _sent
                    ? _run(() => api.resetPassword(_email.text, _code.text, _pw.text), 'Password updated. Please sign in.', done: true)
                    : _run(() => api.forgotPassword(_email.text), 'If the email exists, a code is on its way.'),
            child: Text(_sent ? 'Update password' : 'Send code'),
          ),
        ]),
      ),
    );
  }
}
