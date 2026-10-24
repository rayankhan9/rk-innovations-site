import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../config.dart';
import '../../services/api.dart';
import '../../state/app_state.dart';
import '../../widgets/common.dart';
import 'forgot_screen.dart';
import 'register_screen.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController(), _pw = TextEditingController();
  bool _busy = false, _hide = true;

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    setState(() => _busy = true);
    try {
      await context.read<AppState>().login(_email.text, _pw.text);
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message, error: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _server() async {
    final app = context.read<AppState>();
    final c = TextEditingController(text: app.api.baseUrl);
    final url = await showDialog<String>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Server address'),
        content: Column(mainAxisSize: MainAxisSize.min, children: [
          const Text('Only change this if your organization gave you a different server address.', style: TextStyle(fontSize: 13)),
          const SizedBox(height: 12),
          TextField(controller: c, keyboardType: TextInputType.url, decoration: const InputDecoration(hintText: 'https://attendance.example.com')),
        ]),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, ''), child: const Text('Reset')),
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
          FilledButton(style: FilledButton.styleFrom(minimumSize: const Size(80, 40)), onPressed: () => Navigator.pop(context, c.text), child: const Text('Save')),
        ],
      ),
    );
    if (url == null) return;
    url.isEmpty ? await app.api.resetBaseUrl() : await app.api.setBaseUrl(url);
    if (mounted) {
      setState(() {});
      toast(context, 'Server set to ${app.api.baseUrl}');
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Form(
                key: _form,
                child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  Center(child: ClipRRect(borderRadius: BorderRadius.circular(22), child: Image.asset('assets/branding/icon.png', width: 84, height: 84))),
                  const SizedBox(height: 12),
                  Text(AppConfig.appName, textAlign: TextAlign.center, style: Theme.of(context).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.w700)),
                  Text('Sign in to mark your attendance', textAlign: TextAlign.center, style: TextStyle(color: cs.onSurfaceVariant)),
                  const SizedBox(height: 28),
                  TextFormField(
                    controller: _email,
                    keyboardType: TextInputType.emailAddress,
                    autofillHints: const [AutofillHints.username],
                    decoration: const InputDecoration(labelText: 'Email', prefixIcon: Icon(Icons.email_outlined)),
                    validator: (v) => (v == null || !v.contains('@')) ? 'Enter your email' : null,
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _pw,
                    obscureText: _hide,
                    autofillHints: const [AutofillHints.password],
                    decoration: InputDecoration(
                      labelText: 'Password',
                      prefixIcon: const Icon(Icons.lock_outline),
                      suffixIcon: IconButton(icon: Icon(_hide ? Icons.visibility : Icons.visibility_off), onPressed: () => setState(() => _hide = !_hide)),
                    ),
                    validator: (v) => (v == null || v.isEmpty) ? 'Enter your password' : null,
                    onFieldSubmitted: (_) => _submit(),
                  ),
                  Align(alignment: Alignment.centerRight, child: TextButton(onPressed: () => Navigator.push(context, MaterialPageRoute(builder: (_) => const ForgotScreen())), child: const Text('Forgot password?'))),
                  FilledButton(onPressed: _busy ? null : _submit, child: _busy ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Sign in')),
                  const SizedBox(height: 12),
                  OutlinedButton(
                    style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(48), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10))),
                    onPressed: () => Navigator.push(context, MaterialPageRoute(builder: (_) => const RegisterScreen())),
                    child: const Text('New here? Register with your organization code'),
                  ),
                  const SizedBox(height: 16),
                  TextButton(onPressed: _server, child: const Text('Server settings', style: TextStyle(fontSize: 12))),
                ]),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
